using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Calor.Compiler.Parsing;

/// <summary>
/// Recursive descent parser for Calor source code.
/// </summary>
public sealed class Parser
{
    private static T WithDocumentation<T>(T node, Token startToken) where T : AstNode
    {
        node.DocComment = startToken.DocComment;
        return node;
    }

    private delegate ExpressionNode ExpressionParser(Parser parser);

    private static readonly IReadOnlyDictionary<TokenKind, ExpressionParser> ExpressionParsers =
        new Dictionary<TokenKind, ExpressionParser>
        {
            [TokenKind.IntLiteral] = static parser => parser.ParseIntLiteral(),
            [TokenKind.StrLiteral] = static parser => parser.ParseStringLiteral(),
            [TokenKind.CharLiteral] = static parser => parser.ParseCharLiteral(),
            [TokenKind.BoolLiteral] = static parser => parser.ParseBoolLiteral(),
            [TokenKind.FloatLiteral] = static parser => parser.ParseFloatLiteral(),
            [TokenKind.DecimalLiteral] = static parser => parser.ParseDecimalLiteral(),
            [TokenKind.Identifier] = static parser => parser.ParseReference(),
            [TokenKind.OpenParen] = static parser => parser.ParseParenExpressionOrInlineLambda(),
            [TokenKind.OpenBrace] = static parser => parser.ParseCollectionInitializer(),
            [TokenKind.Match] = static parser => parser.ParseMatchExpression(),
            [TokenKind.New] = static parser => parser.ParseNewExpression(),
            [TokenKind.This] = static parser => parser.ParseThisExpression(),
            [TokenKind.Base] = static parser => parser.ParseBaseExpression(),
            [TokenKind.Await] = static parser => parser.ParseAwaitExpression(),
            [TokenKind.RangeOp] = static parser => parser.ParseRangeExpression(),
            [TokenKind.Throw] = static parser => parser.ParseThrowExpression(),
            [TokenKind.Hash] = static parser => parser.ParseSelfRef(),
            [TokenKind.At] = static parser => parser.ParseVerbatimIdentifier(),
            [TokenKind.Minus] = static parser => parser.ParsePrefixUnary(),
            [TokenKind.Exclamation] = static parser => parser.ParsePrefixUnary(),
            [TokenKind.Tilde] = static parser => parser.ParsePrefixUnary(),
            [TokenKind.Caret] = static parser => parser.ParseIndexFromEnd(),
        };

    internal static IReadOnlyCollection<TokenKind> RegisteredExpressionStartTokens
        => ExpressionParsers.Keys.ToArray();

    private readonly List<Token> _tokens;
    private readonly DiagnosticBag _diagnostics;
    private int _position;
    private bool _insideRefinementPredicate;
    private int _quantifierRangeDelimiterPosition = -1;
    private readonly Dictionary<ReferenceNode, TextSpan> _groupedReferenceSpans = new();

    /// <summary>
    /// The identifier-token start of every dotted name this parse read as one reference
    /// (<c>x.Field</c>), so the value-read pass can name the token to split on reparse.
    /// </summary>
    private readonly Dictionary<ReferenceNode, int> _dottedReferenceTokenStarts = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// On the lowering reparse only: the token starts of dotted names whose first segment
    /// names a value in scope. Each becomes the member-access chain the spaced spelling
    /// <c>x . Field</c> produces (see <see cref="Parse"/>).
    /// </summary>
    private readonly IReadOnlySet<int>? _dottedValueReadStarts;

    /// <summary>
    /// Stack of <c>eff</c> binder scopes, innermost last. A scope is pushed when a
    /// declaration form that may bind effect variables (<c>§F</c>, <c>§AF</c>,
    /// <c>§MT</c>, <c>§AMT</c>, and an interface member's <c>§MT</c>) has parsed its
    /// type-parameter list, and popped in a <c>finally</c> when that declaration is
    /// done. Nesting is real — an interface member sits inside a <c>§IFACE</c> and a
    /// <c>§LAM</c> sits inside a function body — which is why this is a stack and not
    /// the spike prototype's single list reset by every type-parameter list.
    /// </summary>
    private readonly List<IReadOnlyList<EffectParameterInfo>> _effectVariableScopes = new();

    /// <summary>
    /// Calor0405 recovery for the <b>declaration that just finished parsing</b>: a
    /// <c>§E{…}</c> now sitting at the start of the following line, at a position whose
    /// enclosing loop has no <c>§E</c> arm to fall through to. Executed baselines
    /// <b>Z1</b> (<c>§FLD</c> ⏎ <c>§E</c>, 4× Calor0100) and <b>Z2</b> (<c>§B</c> ⏎
    /// <c>§E</c>, 4× Calor0100).
    /// </summary>
    /// <remarks>
    /// <para>Called at the END of <c>ParseBindStatement</c> and <c>ParseClassField</c>,
    /// which is where §3.1 puts it — "each of the six insertion points carries the
    /// Calor0405 recovery on its non-adjacent branch". An earlier draft of this slice
    /// hoisted it into the statement and class-member LOOPS instead, on the reasoning
    /// that the production could not see a token it had already returned past. That
    /// reasoning was wrong — <c>Current</c> IS the <c>§E</c> when these productions
    /// return — and hoisting it cost three separate defects, all found in review:</para>
    /// <list type="number">
    /// <item>a broken multi-line call (<c>§C{Helper}</c> ⏎ <c>§A INT:1</c> ⏎
    /// <c>§E{cw}</c>) reached the statement loop and was told its row was on the wrong
    /// line, extending Calor0405 into an argument list — which §3.3 forbids outright;</item>
    /// <item>a failed inline signature left the function's own perfectly correct
    /// <c>§E{…}</c> line in statement position, where it was reported as misplaced
    /// (executed case <b>X4</b>);</item>
    /// <item>the "which declaration?" subject had to be remembered in a parser field,
    /// which leaked across scopes and could name a field in a different class.</item>
    /// </list>
    /// <para>Anchoring the recovery to the production that owns the row removes all
    /// three by construction: it cannot fire unless a <c>§B</c> or <c>§FLD</c> was the
    /// thing immediately before the <c>§E</c>, and the subject is the declaration in
    /// hand rather than a remembered one.</para>
    /// </remarks>
    private void TryRecoverTrailingEffectRow(string subject, string subjectKind)
    {
        if (!Check(TokenKind.Exclamation))
            return;

        // Calor0405's whole claim is that a row is on the wrong LINE. A §E still on
        // the declaration's own line was already taken as its row by
        // TryParseSameLineRow; anything else here belongs to the enclosing production.
        if (_position > 0 && PreviousToken.Span.Line == Current.Span.Line)
            return;

        ReportMisplacedEffectRow(subject, subjectKind);
    }


    /// <summary>
    /// The most recently parsed <c>§IF</c> opener token. Used by
    /// <see cref="ParseStatement"/>'s stray-<c>§EI</c>/<c>§EL</c> recovery
    /// to build a re-indentation fix that aligns a misaligned else clause
    /// with the <c>§IF</c> it most plausibly belongs to.
    /// </summary>
    private Token? _lastIfToken;

    // #1485: indentation column of the §M line (-1 before ParseModule). A
    // declaration written at the module's own column is the legacy flat layout.
    private int _moduleIndentColumn = -1;

    /// <summary>
    /// Phase 4d -- TokenKinds whose textual form is a legacy structural
    /// closing tag (<c>§/M</c>, <c>§/F</c>, <c>§/CL</c>, …) that indent
    /// form has fully replaced. Encountering any of these in the token
    /// stream produces a <c>Calor0830 LegacyCloserForm</c> error.
    ///
    /// Closers that still carry payload -- <see cref="TokenKind.EndDo"/>
    /// (carries the do-while condition), <see cref="TokenKind.EndCase"/>
    /// (match case delimiter), and the preprocessor <c>§/PP</c> -- are
    /// intentionally NOT in this set. Inline expression closers
    /// (<c>§/C</c>, <c>§/T</c>, <c>§/NEW</c>, <c>§/A</c>, <c>§/LIST</c>,
    /// <c>§/DICT</c>, <c>§/HSET</c>, <c>§/ARR</c>, <c>§/LAM</c>,
    /// <c>§/THIS</c>, <c>§/BASE</c>, <c>§/INIT</c>, …) are also kept
    /// because they can legitimately appear inline within a single
    /// expression where no dedent is available to terminate them.
    /// </summary>
    private static readonly HashSet<TokenKind> StructuralLegacyClosers = new()
    {
        TokenKind.EndModule,
        TokenKind.EndFunc,
        TokenKind.EndAsyncFunc,
        TokenKind.EndMatch,
        TokenKind.EndFor,
        TokenKind.EndWhile,
        TokenKind.EndIf,
        TokenKind.EndForeach,
        TokenKind.EndEachKV,
        TokenKind.EndInterface,
        TokenKind.EndMethod,
        TokenKind.EndClass,
    };

    public Parser(IEnumerable<Token> tokens, DiagnosticBag diagnostics)
    {
        _tokens = tokens.ToList();
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    private Parser(List<Token> tokens, DiagnosticBag diagnostics, IReadOnlySet<int> dottedValueReadStarts)
    {
        _tokens = tokens;
        _diagnostics = diagnostics;
        _dottedValueReadStarts = dottedValueReadStarts;
    }

    private Token Current => Peek(0);
    private Token Lookahead => Peek(1);

    private Token Peek(int offset)
    {
        var index = _position + offset;
        return index < _tokens.Count ? _tokens[index] : _tokens[^1];
    }

    private bool IsAtEnd => Current.Kind == TokenKind.Eof;

    private Token Advance()
    {
        var token = Current;
        if (!IsAtEnd)
        {
            _position++;
        }
        return token;
    }

    private bool Check(TokenKind kind) => Current.Kind == kind;

    private bool Match(TokenKind kind)
    {
        if (Check(kind))
        {
            Advance();
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------
    // #1485 -- indentation ownership of block bodies.
    //
    // A block-opening clause (§IF/§EI/§EL, §L, §WH, §DO, §EACH, §EACHKV,
    // §USE, §TR/§CA/§FI, §SYNC, §UNSAFE, §FIXED, §W/§K) owns only the lines
    // indented deeper than the line it sits on (plus any statement written on
    // its own line). An EMPTY body is legal: the lexer then emits no Indent
    // after the header, so the next line -- a same-column sibling or the
    // enclosing block's Dedent -- belongs to the enclosing block. Before this,
    // the body loops ran until the next Dedent, so a sibling statement after
    // an empty clause was silently parsed into the clause and the enclosing
    // block's Dedent was consumed as the clause's end.
    //
    // Flat closer form (body at the opener's own column, terminated by an
    // explicit closer such as §/TR{id}) is still accepted: when the body is
    // not indented and a matching closer follows at the opener's column, the
    // block keeps the old run-until-closer behavior.
    // ------------------------------------------------------------------

    /// <summary>Ownership state for one block body (#1485).</summary>
    /// <remarks>
    /// <c>IndentColumn</c> is the indentation of the opener's line;
    /// <c>HeaderLine</c> is the line the header ends on (later than the
    /// opener's line when a bracketed header spans lines). Content on the
    /// header line belongs to the body.
    /// </remarks>
    private readonly record struct BlockOwner(Token Opener, int IndentColumn, int HeaderLine, bool Flat);

    /// <summary>
    /// #1485 -- a chain continuation (§EI/§EL, §CA/§FI, §PPE) belongs to the
    /// chain opened by <paramref name="chainOwner"/> unless its line sits at a
    /// shallower column. Inside brackets the lexer emits no Dedent, so this
    /// column test is what keeps an outer §EL from attaching to an inner §IF.
    /// </summary>
    private bool ContinuesChain(BlockOwner chainOwner)
        => LineIndentColumnAt(_position) >= chainOwner.IndentColumn;

    /// <summary>
    /// The indentation column of the source line holding the token at
    /// <paramref name="index"/>: the column of the first real token on that
    /// line. Works inside brackets, where the lexer emits no Indent/Dedent.
    /// </summary>
    private int LineIndentColumnAt(int index)
    {
        if (index < 0 || index >= _tokens.Count) return 0;
        var line = _tokens[index].Span.Line;
        var column = _tokens[index].Span.Column;
        for (int i = index - 1; i >= 0; i--)
        {
            var t = _tokens[i];
            if (t.Kind is TokenKind.Dedent or TokenKind.Indent) continue;
            if (t.Span.Line != line) break;
            column = t.Span.Column;
        }
        return column;
    }

    private int IndexOfRecentToken(Token token)
    {
        for (int i = Math.Min(_position, _tokens.Count - 1); i >= 0; i--)
        {
            if (_tokens[i].Span.Start == token.Span.Start && _tokens[i].Kind == token.Kind)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Begins the body of the block opened by <paramref name="opener"/> (call
    /// after the opener's header has been parsed). <paramref name="closer"/>
    /// and <paramref name="id"/> identify the explicit closer that would put
    /// the block in flat closer form. <paramref name="siblingKinds"/> are
    /// openers that, at the opener's own column, end the search for that
    /// closer (e.g. a following §K ends an empty §K).
    /// </summary>
    private BlockOwner BeginBlockBody(Token opener, TokenKind closer, string? id, params TokenKind[] siblingKinds)
    {
        var openerIndex = IndexOfRecentToken(opener);
        var column = openerIndex >= 0 ? LineIndentColumnAt(openerIndex) : opener.Span.Column;
        var headerLine = opener.Span.Line;
        for (int i = _position - 1; i >= 0; i--)
        {
            if (_tokens[i].Kind is TokenKind.Dedent or TokenKind.Indent) continue;
            headerLine = Math.Max(headerLine, _tokens[i].Span.Line);
            break;
        }
        var owner = new BlockOwner(opener, column, headerLine, Flat: false);

        // An indented body is the ordinary indent form. (A body that starts on
        // the header's line may still continue in flat closer form, so it gets
        // the closer scan below.)
        if (Check(TokenKind.Dedent) || Check(TokenKind.Eof)
            || Current.Span.Line != headerLine && LineIndentColumnAt(_position) > column)
            return owner;

        return owner with { Flat = false };
    }

    /// <summary>
    /// True while the current token can start a statement of the body owned by
    /// <paramref name="owner"/>. Dedent/Eof return true so the caller's
    /// existing block-end checks decide.
    /// </summary>
    private bool IsInBlockBody(BlockOwner owner)
        => Check(TokenKind.Dedent)
           || Check(TokenKind.Eof)
           || Current.Span.Line == owner.HeaderLine
           || LineIndentColumnAt(_position) > owner.IndentColumn;

    /// <summary>
    /// Declaration bodies (§F/§AF, §CL, §EN) written at the module's own column
    /// are the legacy flat layout (every line at column 1, no closers): there a
    /// function body or class member sits at the declaration's own column, and
    /// such a line could not belong to the module anyway. In that layout a
    /// declaration body ends at an unindented line only when the line opens a
    /// sibling declaration. Indented declarations use the ordinary rule, so an
    /// empty nested §CL does not adopt the next same-column §MT.
    /// </summary>
    private bool IsInDeclarationBody(BlockOwner owner, bool interopIsSibling)
        => IsInBlockBody(owner)
           || owner.IndentColumn <= _moduleIndentColumn && !IsSiblingDeclarationOpener(interopIsSibling);

    private bool IsSiblingDeclarationOpener(bool interopIsSibling)
        => Check(TokenKind.Func)
           || Check(TokenKind.Class) || Check(TokenKind.Record) || Check(TokenKind.Interface)
           || Check(TokenKind.Enum)
           || Check(TokenKind.Delegate)
           || interopIsSibling && Check(TokenKind.CSharpInterop);

    /// <summary>True when the current Dedent closes the indented body of <paramref name="owner"/>.</summary>
    private bool IsOwnedDedent(BlockOwner owner)
        => Check(TokenKind.Dedent) && Current.IndentationDepth == owner.Opener.IndentationDepth;

    /// <summary>
    /// Ends the body owned by <paramref name="owner"/>. An indented body is
    /// closed by its own Dedent (or the explicit closer); an empty or
    /// same-line body consumes nothing, leaving a same-column sibling or the
    /// enclosing block's Dedent for the enclosing block. A returned
    /// synthetic Dedent token marks an implicit end.
    /// </summary>
    private Token ExpectOwnedBlockEnd(TokenKind closer, BlockOwner owner)
    {
        if (Check(closer) || IsOwnedDedent(owner))
            return ExpectBlockEnd(closer);
        if (Check(TokenKind.Eof))
            return Current;
        if (Check(TokenKind.Dedent) || LineIndentColumnAt(_position) <= owner.IndentColumn)
            return ImplicitBlockEndToken();
        // A deeper, unowned token (e.g. a statement at §K column inside a
        // match): keep the old diagnostic.
        return ExpectBlockEnd(closer);
    }

    private Token ImplicitBlockEndToken()
    {
        for (int i = _position - 1; i >= 0; i--)
        {
            if (_tokens[i].Kind is not (TokenKind.Dedent or TokenKind.Indent))
                return new Token(TokenKind.Dedent, "", _tokens[i].Span);
        }
        return new Token(TokenKind.Dedent, "", Current.Span);
    }

    /// <summary>
    /// #1485 -- like <see cref="ConsumeDedentBeforeChain"/>, but consumes the
    /// Dedent only when it closes <paramref name="owner"/>'s own indented
    /// body. A Dedent from a shallower level belongs to an enclosing block,
    /// and the continuation after it (e.g. an outer §CA or §EL) is not ours.
    /// </summary>
    private void ConsumeOwnedDedentBeforeChain(BlockOwner owner, params TokenKind[] continuations)
    {
        if (IsOwnedDedent(owner) && Array.IndexOf(continuations, Peek(1).Kind) >= 0)
            Advance();
    }

    /// <summary>
    /// #1485 -- statement list for a block body owned by <paramref name="owner"/>;
    /// stops at the explicit <paramref name="closer"/>, any Dedent, or the
    /// first statement that is not indented under the opener.
    /// </summary>
    private List<StatementNode> ParseOwnedStatementBlock(BlockOwner owner, TokenKind closer)
    {
        var statements = new List<StatementNode>();
        while (!IsAtEnd && !Check(closer) && !Check(TokenKind.Dedent) && IsInBlockBody(owner))
        {
            var stmt = ParseStatement();
            if (stmt != null)
                statements.Add(stmt);
        }
        return statements;
    }

    private Token Expect(TokenKind kind)
    {
        if (Check(kind))
        {
            return Advance();
        }

        _diagnostics.ReportUnexpectedToken(Current.Span, kind, Current.Kind);
        return new Token(kind, "", Current.Span);
    }

    /// <summary>
    /// Phase 3 (indent-aware) -- returns true if the current token is a block
    /// terminator: either the explicit closer (e.g. EndFunc) or a Dedent
    /// emitted by the indent-aware lexer.
    ///
    /// When the lexer is in closer-only mode (Tokenize), Dedent never appears
    /// and this reduces to Check(explicitCloser). When the lexer is in
    /// indent-aware mode (TokenizeWithIndent), this accepts either form.
    /// </summary>
    private bool IsBlockEnd(TokenKind explicitCloser)
        => Check(explicitCloser) || Check(TokenKind.Dedent) || Check(TokenKind.Eof);

    private bool IsBlockEndAtIndent(TokenKind explicitCloser, Token blockStart)
        => Check(explicitCloser)
           || Check(TokenKind.Eof)
           || Check(TokenKind.Dedent)
           && Current.Span.Column <= blockStart.Span.Column;

    private bool ConsumeContinuationDedent(Token blockStart)
    {
        if (!Check(TokenKind.Dedent)
            || Current.Span.Column <= blockStart.Span.Column)
        {
            return false;
        }

        Advance();
        return true;
    }

    /// <summary>
    /// Phase 3 (indent-aware) -- overload accepting two alternative closers
    /// (used for sites like §F/§AF that share a body parser).
    /// </summary>
    private bool IsBlockEnd(TokenKind closer1, TokenKind closer2)
        => Check(closer1) || Check(closer2) || Check(TokenKind.Dedent) || Check(TokenKind.Eof);

    /// <summary>
    /// True when the current token opens a type declaration (§CL / §IFACE / §EN /
    /// §D / §DEL, or a §CSHARP block) at EXACTLY <paramref name="ownerColumn"/> - the column of the
    /// §IFACE whose body we are reading. Used by
    /// <see cref="ParseInterfaceDefinition"/> to end an empty interface body at a
    /// same-column sibling, which the lexer cannot mark with a Dedent (#903
    /// cluster 2). The column test is load-bearing (review M2): an INDENTED type
    /// opener is not a sibling, and silently reparenting it to the enclosing
    /// scope would accept, with no diagnostic, a nesting the language does not
    /// have. Such an opener falls through to the loop's error arm as before.
    /// </summary>
    private bool IsSiblingTypeOpenerAt(int ownerColumn)
        => Current.Span.Column == ownerColumn
           && (Check(TokenKind.Class) || Check(TokenKind.Record)
               || Check(TokenKind.Interface)
               || Check(TokenKind.Enum)
               || Check(TokenKind.Delegate)
               // Review m1: a §CSHARP block at the interface's OWN column is a
               // sibling too - #717's rewrap emits exactly that when a sibling
               // type could not be converted, and it was being absorbed as an
               // interface member with no diagnostic. Deeper §CSHARP is still a
               // member (an interface can carry interop).
               || Check(TokenKind.CSharpInterop)
               // 0.30: interface members are indented under the interface, so any other
               // declaration at its own column (a module function, field, nested module,
               // pp block...) is a sibling that follows an empty marker interface.
               || Check(TokenKind.Func)
               || Check(TokenKind.Property) || Check(TokenKind.FieldDef)
               || Check(TokenKind.Constructor) || Check(TokenKind.Event)
               || Check(TokenKind.OperatorOverload) || Check(TokenKind.Module)
               || Check(TokenKind.Preprocessor) || Check(TokenKind.Using));

    /// <summary>
    /// Phase 3 (indent-aware) -- consumes the block-end marker and returns
    /// the consumed token (for AST span end calculation).
    ///
    /// Behavior:
    /// 1. If the current token is the explicit closer, consume and return it.
    /// 2. If the current token is a Dedent and the explicit closer follows
    ///    after any number of contiguous Dedents (closer-form code parsed
    ///    via the indent-aware lexer -- multiple lexer-level dedents may
    ///    correspond to a single block-end at the parser level), consume
    ///    ALL of those Dedents and then the explicit closer.
    /// 3. Otherwise (indent-only mode, no explicit closer present),
    ///    consume a single Dedent and return it.
    /// 4. If neither Dedent nor closer is present, error via Expect.
    /// </summary>
    private Token ExpectBlockEnd(TokenKind explicitCloser)
    {
        if (Check(explicitCloser))
        {
            ReportLegacyCloser(Current);
            return Advance();
        }
        if (Check(TokenKind.Dedent))
        {
            // Look ahead past any contiguous Dedents for the explicit closer
            int lookahead = 1;
            while (Peek(lookahead).Kind == TokenKind.Dedent) lookahead++;
            if (Peek(lookahead).Kind == explicitCloser)
            {
                // Closer-form: consume all dedents, then the closer.
                while (Check(TokenKind.Dedent)) Advance();
                ReportLegacyCloser(Current);
                return Advance();
            }
            // Indent-only: consume the single Dedent that ends this block.
            return Advance();
        }
        if (Check(TokenKind.Eof))
        {
            // Indent-only at EOF: any unclosed block ends implicitly here.
            // Do NOT consume the Eof -- callers and the outer Parse loop
            // still need it to recognize end-of-input.
            return Current;
        }
        return Expect(explicitCloser);
    }

    private Token ExpectBlockEndAtIndent(
        TokenKind explicitCloser,
        Token blockStart)
    {
        while (ConsumeContinuationDedent(blockStart))
        {
        }

        return ExpectBlockEnd(explicitCloser);
    }

    /// <summary>
    /// Phase 4d -- emit <c>Calor0830</c> unconditionally whenever the
    /// parser is about to consume a structural legacy closer token.
    /// Closer form was removed from the language in Phase 4d, so any
    /// occurrence of one of the <see cref="StructuralLegacyClosers"/>
    /// tokens is an error. The parser still consumes the token (the
    /// closer-form code paths above remain in place so downstream
    /// productions see the structure they expect) and reports the
    /// diagnostic via the bag.
    /// </summary>
    private void ReportLegacyCloser(Token closerToken)
    {
        if (!StructuralLegacyClosers.Contains(closerToken.Kind)) return;

        var fix = BuildLegacyCloserRemovalFix(closerToken);

        _diagnostics.ReportErrorWithFix(
            closerToken.Span,
            DiagnosticCode.LegacyCloserForm,
            $"Legacy structural closing tag '{closerToken.Text}' was removed in Phase 4d and is no longer accepted. " +
            "A block now ends at the dedent of its body, so delete the closer line -- " +
            "the indentation already marks where the block ends.",
            fix);
    }

    /// <summary>
    /// Builds the <see cref="SuggestedFix"/> that auto-heals a legacy
    /// structural closer by deleting only its token and payload. The edit spans from
    /// the closer's column to just past the closer keyword and any
    /// optional <c>{id}</c> payload (e.g. <c>§/F{f1}</c>), replacing it with
    /// an empty string. Indent-only Calor tolerates the resulting blank line,
    /// so the healed source compiles. Consumed by the LSP code-action handler
    /// and the <c>calor_check</c> MCP tool's <c>apply</c> path.
    /// </summary>
    private SuggestedFix BuildLegacyCloserRemovalFix(Token closerToken)
    {
        var span = closerToken.Span;
        int endLine = span.Line;
        int endColumn = span.Column + span.Length;

        // Extend the removal over an optional {id} payload so the whole
        // closer construct is deleted, not just the keyword. Structural
        // closers carry at most a single brace group (the echoed opener id).
        if (Peek(1).Kind == TokenKind.OpenBrace)
        {
            int depth = 0;
            int i = 1;
            while (Peek(i).Kind != TokenKind.Eof)
            {
                var kind = Peek(i).Kind;
                if (kind == TokenKind.OpenBrace)
                {
                    depth++;
                }
                else if (kind == TokenKind.CloseBrace)
                {
                    depth--;
                    if (depth == 0)
                    {
                        var closeSpan = Peek(i).Span;
                        endLine = closeSpan.Line;
                        endColumn = closeSpan.Column + closeSpan.Length;
                        break;
                    }
                }
                i++;
            }
        }

        var filePath = _diagnostics.CurrentFilePath ?? string.Empty;
        return new SuggestedFix(
            $"Remove the legacy closer '{closerToken.Text}' (indentation already ends the block)",
            TextEdit.Replace(filePath, span.Line, span.Column, endLine, endColumn, string.Empty),
            isSafeToApply: true);
    }

    public ModuleNode Parse()
    {
        var errorsBefore = _diagnostics.Errors.Count;
        var module = ParseAndValidateModule();
        if (_dottedValueReadStarts is not null || _dottedReferenceTokenStarts.Count == 0)
            return module;

        // The lexer reads `x.Field` as one dotted name, the form that also names namespaces
        // and types (`System.Math.PI`). As in C# simple-name lookup, a value in scope (a
        // local, a parameter, or a field, property or event of an enclosing type) wins over
        // a namespace or type. A dotted name whose first segment is a value is therefore a
        // member read, and every later pass (binding, typing, effects, nullability,
        // contracts, codegen, tooling) must see exactly what the spaced `x . Field` gives.
        // Scope is known only once the module is parsed (members may follow their uses), so
        // the module is parsed again with those names split into the spaced form's chain.
        var valueReads = DottedValueReadResolver.Resolve(module, _dottedReferenceTokenStarts);
        if (valueReads.Count == 0)
            return module;
        var reparseDiagnostics = new DiagnosticBag();
        var lowered = new Parser(_tokens, reparseDiagnostics, valueReads).ParseAndValidateModule();
        // Same tokens, same errors: anything else would mean the split changed the parse.
        return reparseDiagnostics.Errors.Count == _diagnostics.Errors.Count - errorsBefore ? lowered : module;
    }

    private ModuleNode ParseAndValidateModule()
    {
        var module = ParseModule();
        ValidateDefaultConstraintOwners(module);
        ValidateEffectBoundaries(module);
        return module;
    }

    private void ValidateEffectBoundaries(ModuleNode module)
    {
        if (_diagnostics.HasErrors)
            return;
        foreach (var function in module.Functions)
            ValidateOwnEffectRow(function.Name, function.Span, function.Effects,
                function.Visibility is not (Visibility.Private or Visibility.Internal));
        foreach (var extension in module.EnumExtensions)
            foreach (var function in extension.Methods)
                ValidateOwnEffectRow(function.Name, function.Span, function.Effects,
                    function.Visibility is not (Visibility.Private or Visibility.Internal));
        foreach (var owner in Analysis.CallGraphAnalysis.EnumerateClasses(module))
        {
            foreach (var method in Analysis.CallGraphAnalysis.EnumerateMethods(owner))
                ValidateOwnEffectRow(method.Name, method.Span, method.Effects,
                    method.IsAbstract || method.IsExtern
                    || method.Visibility is not (Visibility.Private or Visibility.Internal));
            foreach (var op in Analysis.CallGraphAnalysis.EnumerateOperators(owner))
                ValidateOwnEffectRow($"operator {op.OperatorToken}", op.Span, op.Effects, required: true);
        }
        foreach (var iface in Analysis.CallGraphAnalysis.EnumerateInterfaces(module))
            foreach (var method in iface.Methods)
                ValidateOwnEffectRow(method.Name, method.Span, method.Effects, required: true);
        foreach (var declaration in Analysis.CallGraphAnalysis.EnumerateDelegates(module))
            ValidateOwnEffectRow(declaration.Name, declaration.Span, declaration.Effects, required: true);
    }

    private void ValidateOwnEffectRow(string name, TextSpan span, EffectsNode? row, bool required)
    {
        if (required && row == null)
            _diagnostics.ReportError(span, DiagnosticCode.MissingBoundaryEffectRow,
                $"Boundary '{name}' requires an explicit effect row. Write '!' for Pure or '! effects' before its body.");
        // 0.30: an inferred row cannot be unknown (Calor0410), so ! unknown is the one
        // own row a private/internal body may write.
        else if (!required && row != null && !(row.IsUnknown && row.EffectVariables.Count == 0))
            _diagnostics.ReportError(row.Span, DiagnosticCode.NonPublicEffectRow,
                $"Private/internal body '{name}' infers its effects. Remove its own effect row "
                + "(only ! unknown is allowed there); callback type rows remain explicit.");
    }

    private void ValidateDefaultConstraintOwners(ModuleNode module)
    {
        foreach (var function in module.Functions)
            ValidateDefaultConstraints(
                function.TypeParameters,
                isLegal: false,
                owner: "a function");
        foreach (var extension in module.EnumExtensions)
        {
            foreach (var method in extension.Methods)
                ValidateDefaultConstraints(
                    method.TypeParameters,
                    isLegal: false,
                    owner: "an enum extension method");
        }
        foreach (var iface in module.Interfaces)
            ValidateInterfaceDefaultConstraints(iface);
        foreach (var type in module.Classes)
            ValidateClassDefaultConstraints(type);
        foreach (var block in module.TypePreprocessorBlocks)
            ValidateTypePreprocessorDefaultConstraints(block);
    }

    private void ValidateInterfaceDefaultConstraints(InterfaceDefinitionNode node)
    {
        ValidateDefaultConstraints(
            node.TypeParameters,
            isLegal: false,
            owner: "an interface");
        foreach (var method in node.Methods)
            ValidateDefaultConstraints(
                method.TypeParameters,
                isLegal: false,
                owner: "an interface method");
    }

    private void ValidateClassDefaultConstraints(ClassDefinitionNode node)
    {
        ValidateDefaultConstraints(
            node.TypeParameters,
            isLegal: false,
            owner: node.IsStruct ? "a struct" : "a class");
        foreach (var method in node.Methods)
        {
            var isExplicitInterfaceImplementation =
                method.Name.Contains('.', StringComparison.Ordinal);
            ValidateDefaultConstraints(
                method.TypeParameters,
                method.IsOverride || isExplicitInterfaceImplementation,
                method.IsOverride
                    ? "an override method"
                    : isExplicitInterfaceImplementation
                        ? "an explicit interface implementation"
                        : "an ordinary method");
        }
        foreach (var block in node.PreprocessorBlocks)
            ValidateMemberPreprocessorDefaultConstraints(block);
        foreach (var nested in node.NestedClasses)
            ValidateClassDefaultConstraints(nested);
        foreach (var nested in node.NestedInterfaces)
            ValidateInterfaceDefaultConstraints(nested);
    }

    private void ValidateMemberPreprocessorDefaultConstraints(
        MemberPreprocessorBlockNode block)
    {
        foreach (var method in block.Methods)
        {
            ValidateDefaultConstraints(
                method.TypeParameters,
                method.IsOverride
                    || method.Name.Contains('.', StringComparison.Ordinal),
                "a conditional method");
        }
        if (block.ElseBranch != null)
            ValidateMemberPreprocessorDefaultConstraints(block.ElseBranch);
    }

    private void ValidateTypePreprocessorDefaultConstraints(
        TypePreprocessorBlockNode block)
    {
        foreach (var iface in block.Interfaces)
            ValidateInterfaceDefaultConstraints(iface);
        foreach (var type in block.Classes)
            ValidateClassDefaultConstraints(type);
        foreach (var nested in block.NestedBlocks)
            ValidateTypePreprocessorDefaultConstraints(nested);
        if (block.ElseBranch != null)
            ValidateTypePreprocessorDefaultConstraints(block.ElseBranch);
    }

    private void ValidateDefaultConstraints(
        IReadOnlyList<TypeParameterNode> typeParameters,
        bool isLegal,
        string owner)
    {
        if (isLegal)
            return;

        foreach (var typeParameter in typeParameters)
        {
            foreach (var constraint in typeParameter.Constraints.Where(
                         constraint => constraint.Kind == TypeConstraintKind.Default))
            {
                _diagnostics.ReportError(
                    constraint.Span,
                    DiagnosticCode.InvalidDefaultConstraintOwner,
                    $"The 'default' constraint on '{typeParameter.Name}' is only legal on override methods or explicit interface implementations, not {owner}.");
            }
        }
    }

    /// <summary>
    /// Phase 3 -- returns true when the current token starts a sibling class /
    /// interface member (method, property, field, ctor, op, indexer, event,
    /// nested type) at the parent's indent level. Used by inline-signature
    /// method/property parsers to terminate an implicit empty body when the
    /// next line is another sibling member rather than an indented body
    /// element. In closer-form code the explicit closer terminates the body
    /// first; in indent-form code with empty bodies there is no Indent →
    /// Dedent pair, so we must look ahead.
    /// </summary>
    private bool IsClassMemberOpener()
        => Check(TokenKind.Module)
            || Check(TokenKind.Property) || Check(TokenKind.FieldDef)
            || Check(TokenKind.Constructor) || Check(TokenKind.OperatorOverload) || IsNativeIndexerHeader() || Check(TokenKind.Event)
            || Check(TokenKind.Class) || Check(TokenKind.Record) || Check(TokenKind.Interface)
            || Check(TokenKind.Enum) || Check(TokenKind.Delegate)
            || Check(TokenKind.Extends) || Check(TokenKind.Implements);

    private bool IsNativeIndexerHeader()
    {
        if (!Check(TokenKind.This) || Peek(1).Kind != TokenKind.OpenBracket)
            return false;
        var depth = 0;
        for (var offset = 1; Peek(offset).Kind != TokenKind.Eof; offset++)
        {
            if (Peek(offset).Kind == TokenKind.OpenBracket) depth++;
            else if (Peek(offset).Kind == TokenKind.CloseBracket && --depth == 0)
                return Peek(offset + 1).Kind == TokenKind.Arrow;
        }
        return false;
    }

    /// <summary>
    /// Phase 3 -- block-end check for class/interface members. Returns true
    /// when the member body should terminate: explicit closer, Dedent, or
    /// a sibling member opener (implicit empty body -- no Indent→Dedent
    /// pair was produced by the lexer because the body had no indented
    /// content).
    /// </summary>
    private bool IsMemberBlockEnd(TokenKind explicitCloser)
        => IsBlockEnd(explicitCloser) || IsClassMemberOpener();

    private bool IsModuleFunctionSibling(Token owner)
        => Check(TokenKind.Func)
           && Current.Span.Column <= owner.Span.Column;

    /// <summary>
    /// Phase 3 -- consume the end of a class / interface member body and
    /// (if the explicit closer was present) any post-closer attributes.
    /// Returns true when the closer was consumed; false when the body
    /// ended implicitly (caller should skip the ParseAttributes /
    /// mismatched-id check). The <paramref name="signatureStart"/> is
    /// the span used when the body ended implicitly.
    ///
    /// <paramref name="hasBodyContent"/> controls Dedent handling: when
    /// true the trailing Dedent terminates the member body and is
    /// consumed; when false the body had no indented content so any
    /// Dedent belongs to the parent block and must be left alone.
    /// </summary>
    private bool TryExpectMemberBlockEnd(TokenKind explicitCloser, Token signatureStart, bool hasBodyContent, out Token endToken)
    {
        if (Check(explicitCloser))
        {
            endToken = ExpectBlockEnd(explicitCloser);
            return true;
        }
        if (Check(TokenKind.Dedent))
        {
            if (hasBodyContent)
            {
                endToken = ExpectBlockEnd(explicitCloser);
                return true;
            }
            // No body content was parsed -- this Dedent belongs to the parent.
            endToken = signatureStart;
            return false;
        }
        if (IsClassMemberOpener() || IsModuleFunctionSibling(signatureStart))
        {
            endToken = signatureStart;
            return false;
        }
        endToken = ExpectBlockEnd(explicitCloser);
        return true;
    }

    private ModuleNode ParseModule()
    {
        var startToken = Expect(TokenKind.Module);
        _moduleIndentColumn = LineIndentColumnAt(_position - 1);
        var attrs = ParseCanonicalModuleAttributes(startToken);
        ValidateIdentityFreeHeader(startToken, attrs, 2);
        var overflowModifier = attrs["_pos1"];
        if (overflowModifier?.StartsWith("overflow=", StringComparison.Ordinal) == true)
            attrs.Add("overflow", overflowModifier["overflow=".Length..].Trim());
        if (attrs["overflow"] is { } overflow && overflow is not ("checked" or "unchecked"))
            _diagnostics.ReportError(attrs.GetSpan("overflow") ?? startToken.Span, DiagnosticCode.InvalidModifier,
                $"Module overflow policy must be 'checked' or 'unchecked', not '{overflow}'.");
        if (attrs["nullable"] is { } nullable && nullable != "oblivious")
            _diagnostics.ReportError(attrs.GetSpan("nullable") ?? startToken.Span, DiagnosticCode.InvalidModifier,
                $"Module nullable policy must be 'oblivious', not '{nullable}'.");

        var id = GenerateParserAutoId("m");
        var moduleName = attrs["_pos0"] ?? "";
        var moduleNameKey = "_pos0";

        if (string.IsNullOrEmpty(moduleName))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "MODULE", "name");
            moduleName = "";
        }

        var usings = new List<UsingDirectiveNode>();
        var interfaces = new List<InterfaceDefinitionNode>();
        var classes = new List<ClassDefinitionNode>();
        var enums = new List<EnumDefinitionNode>();
        var enumExtensions = new List<EnumExtensionNode>();
        var delegates = new List<DelegateDefinitionNode>();
        var functions = new List<FunctionNode>();

        // Extended Features: Module-level metadata
        var issues = new List<IssueNode>();
        var assumptions = new List<AssumeNode>();
        var invariants = new List<InvariantNode>();
        var decisions = new List<DecisionNode>();
        ContextNode? context = null;
        var interopBlocks = new List<CSharpInteropBlockNode>();
        var refinementTypes = new List<RefinementTypeNode>();
        var indexedTypes = new List<IndexedTypeNode>();
        var typePreprocessorBlocks = new List<TypePreprocessorBlockNode>();
        var moduleItems = new List<AstNode>();
        var namespaceScopes = new List<NamespaceScopeInfo>();
        var namespaceName = attrs["namespace"] ?? "";
        namespaceScopes.Add(new NamespaceScopeInfo(GenerateParserAutoId("ns"),
            namespaceName, namespaceName, null, attrs["namespaceOrigin"] != "block", startToken.Span,
            namespaceName.Length == 0 ? NamespaceScopeKind.Global : NamespaceScopeKind.Named));
        string? declaredSemanticsVersion = null;
        var sawSemVer = false;

        while (!IsAtEnd && !IsBlockEndAtIndent(TokenKind.EndModule, startToken))
        {
            if (ConsumeContinuationDedent(startToken))
            {
                continue;
            }
            if (Check(TokenKind.SemVer))
            {
                var semverToken = Advance();
                string? versionText = null;
                if (Check(TokenKind.StrLiteral) && Current.Span.Line == semverToken.Span.Line)
                {
                    var literal = Advance();
                    if (literal.Value is string text && literal.Text == $"\"{text}\"")
                        versionText = text;
                }
                if (!IsAtEnd && Current.Span.Line == semverToken.Span.Line)
                {
                    versionText = null;
                    while (!IsAtEnd && Current.Span.Line == semverToken.Span.Line)
                        Advance();
                }
                if (sawSemVer)
                {
                    // Keyed on "seen", not on the stored value, so a malformed
                    // first directive followed by a valid one is still a duplicate.
                    _diagnostics.ReportError(semverToken.Span,
                        DiagnosticCode.SemanticsVersionInvalidDeclaration,
                        "'semver' may be declared only once per module.");
                }
                else
                {
                    sawSemVer = true;
                    if (SemanticsVersion.ReportDeclaredVersion(_diagnostics, semverToken.Span, versionText))
                    {
                        declaredSemanticsVersion = versionText;
                    }
                }
            }
            else if (Check(TokenKind.Using)
                || Current.Text == "global" && Peek(1).Kind == TokenKind.Using
                || Current.Text == "namespace"
                    && Peek(1).Kind == TokenKind.Using && Peek(1).Span.Line == Current.Span.Line)
            {
                Token? namespacePrefix = Current.Text == "namespace" ? Advance() : null;
                var node = ParseUsingDirective();
                if (namespacePrefix is { } prefix)
                {
                    var scope = namespaceScopes.Single();
                    if (scope.IsGlobal)
                        _diagnostics.ReportError(prefix.Span, DiagnosticCode.UnexpectedToken,
                            "'namespace using' requires a named namespace in the module header.");
                    else
                        ApplyNamespaceMetadata(node, scope.FullName, scope.Id);
                }
                usings.Add(node);
                moduleItems.Add(node);
            }
            else if (Check(TokenKind.Interface))
            {
                var node = ParseInterfaceDefinition();
                interfaces.Add(node);
                moduleItems.Add(node);
            }
            else if (Check(TokenKind.Class) || Check(TokenKind.Record) || Check(TokenKind.Module))
            {
                var node = ParseClassDefinition();
                classes.Add(node);
                moduleItems.Add(node);
            }
            else if (Check(TokenKind.Func))
            {
                var node = ParseFunction();
                functions.Add(node);
                moduleItems.Add(node);
            }
            else if (Check(TokenKind.Delegate))
            {
                var node = ParseDelegateDefinition();
                delegates.Add(node);
                moduleItems.Add(node);
            }
            else if (Check(TokenKind.Enum))
            {
                var node = ParseEnumDefinition();
                enums.Add(node);
                moduleItems.Add(node);
            }
            else if (Check(TokenKind.Assume))
            {
                assumptions.Add(ParseAssume());
            }
            else if (Check(TokenKind.Invariant))
            {
                // Use existing ParseInvariant if available, otherwise parse inline
                var invStartToken = Expect(TokenKind.Invariant);
                var invAttrs = ParseAttributes();
                var message = invAttrs["message"];
                var condition = ParseExpression();
                var invSpan = invStartToken.Span.Union(condition.Span);
                invariants.Add(new InvariantNode(invSpan, condition, message, invAttrs));
            }
            else if (Check(TokenKind.CSharpInterop))
            {
                var node = ParseCSharpInteropBlock();
                interopBlocks.Add(node);
                moduleItems.Add(node);
            }
            else if (Check(TokenKind.CompilerDirective))
            {
                moduleItems.Add(ParseCompilerDirective(moduleItems.All(IsCompilerDirectivePrefix)));
            }
            else if (Check(TokenKind.Preprocessor))
            {
                var node = ParseTypePreprocessorBlock(
                    allowSymbolDirectives: moduleItems.All(IsCompilerDirectivePrefix));
                typePreprocessorBlocks.Add(node);
                moduleItems.Add(node);
            }
            else
            {
                _diagnostics.ReportUnexpectedToken(Current.Span, "SEMVER, USING, NS, IFACE, CLASS, DEL, FUNC, CSHARP, RTYPE, ITYPE, PP, or END_MODULE", Current.Kind);
                Advance();
            }
        }

        var endToken = ExpectBlockEndAtIndent(TokenKind.EndModule, startToken);
        var endAttrs = ParseAttributes();

        // Validate ID matching

        // Phase 3 (indent-aware): in indent mode, the module body loop exits
        // on the first Dedent -- which may be an INNER block's dedent, not the
        // module-ending one. If any non-trivia tokens remain after the module
        // close, they are orphan tags (e.g. an injected §/NEW between the last
        // member and §/M) or stray content. Report them.
        while (!IsAtEnd && Check(TokenKind.Dedent))
        {
            Advance();
        }
        if (!IsAtEnd)
        {
            _diagnostics.ReportUnexpectedToken(Current.Span, TokenKind.Eof, Current.Kind);
        }

        if (namespaceScopes.Count > 0)
        {
            var namespaceDeclarations = interfaces.Cast<AstNode>()
                         .Concat(classes)
                         .Concat(enums)
                         .Concat(enumExtensions)
                         .Concat(delegates)
                         .Concat(functions)
                         .Concat(interopBlocks)
                         .Concat(refinementTypes)
                         .Concat(indexedTypes)
                         .Concat(typePreprocessorBlocks.Where(
                             ContainsNamespaceScopedDeclarations))
                         .ToList();
            var firstDeclarationStart = namespaceDeclarations.Select(item => item.Span.Start)
                .DefaultIfEmpty(int.MaxValue).Min();
            // Leading directive-only blocks belong to the compilation-unit preamble;
            // trailing ones need the enclosing scope to retain their source position.
            namespaceDeclarations.AddRange(typePreprocessorBlocks.Where(block =>
                IsCompilerDirectivePrefix(block) && block.Span.Start > firstDeclarationStart));
            foreach (var declaration in namespaceDeclarations.Where(item =>
                             item.NamespaceIdentity == null
                             && item.NamespaceScopeId == null))
            {
                var scope = namespaceScopes[0];
                ApplyNamespaceMetadata(declaration,
                    scope.FullName,
                    scope.Id);
            }
        }

        var span = startToken.Span.Union(endToken.Span);
        return WithDocumentation(new ModuleNode(span, id, moduleName, usings, interfaces, classes,
            enums, enumExtensions, delegates, functions, attrs,
            issues, assumptions, invariants, decisions, context, interopBlocks, refinementTypes, indexedTypes,
            typePreprocessorBlocks.Count > 0 ? typePreprocessorBlocks : null,
            GetIdentifierSpan(attrs, moduleNameKey, moduleName),
            moduleItems,
            namespaceScopes,
            declaredSemanticsVersion), startToken);
    }

    private AttributeCollection ParseCanonicalModuleAttributes(Token start)
    {
        var attributes = new AttributeCollection();
        var name = Expect(TokenKind.Identifier);
        attributes.Add("_pos0", name.Value as string ?? name.Text, name.Span);
        var namespaceName = "";
        if (Current.Span.Line == start.Span.Line && Check(TokenKind.Identifier)
            && Current.Text is not ("overflow" or "block" or "nullable"))
        {
            var namespaceStart = _position;
            var spelling = ReadInlineTypeToken(validateCanonicalSpelling: false);
            var syntax = SyntaxFactory.ParseName(spelling);
            if (syntax.ContainsDiagnostics
                || syntax.DescendantNodesAndSelf().Any(node => node is GenericNameSyntax or AliasQualifiedNameSyntax))
                _diagnostics.ReportError(GetConsumedSpan(namespaceStart), DiagnosticCode.UnexpectedToken,
                    "The module namespace must be a C# namespace name, with keyword segments escaped using '@'.");
            namespaceName = spelling.Replace("@", "", StringComparison.Ordinal);
        }
        attributes.Add("namespace", namespaceName);
        if (Current.Span.Line == start.Span.Line && Current.Text == "block")
        {
            Advance();
            attributes.Add("namespaceOrigin", "block");
        }
        while (Current.Span.Line == start.Span.Line && Current.Text is "overflow" or "nullable")
        {
            var field = Advance();
            var policy = Expect(TokenKind.Identifier);
            if (attributes[field.Text] != null)
                _diagnostics.ReportError(field.Span, DiagnosticCode.InvalidModifier,
                    $"Duplicate module {field.Text} policy.");
            attributes.Add(field.Text, policy.Text, policy.Span);
        }
        if (Current.Span.Line == start.Span.Line && !IsAtEnd && !Check(TokenKind.Dedent))
            _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken,
                $"Invalid header field '{Current.Text}'; expected 'module name [namespace] [block] [overflow checked|unchecked] [nullable oblivious]'.");
        return attributes;
    }

    private static void ApplyNamespaceMetadata(
        AstNode node,
        string namespaceIdentity,
        string namespaceScopeId)
    {
        node.NamespaceIdentity = namespaceIdentity;
        node.NamespaceScopeId = namespaceScopeId;

        if (node is TypeDefinitionNode type)
        {
            ApplyTypeIdentity(type, namespaceIdentity, namespaceScopeId, null);
        }
        else if (node is FunctionNode function)
        {
            var arity = function.TypeParameters.Count;
            function.FullyQualifiedSymbolIdentity =
                $"global::{(string.IsNullOrEmpty(namespaceIdentity) ? "" : namespaceIdentity + ".")}" +
                function.Name +
                (arity > 0 ? $"`{arity}" : "");
        }
        else if (node is TypePreprocessorBlockNode preprocessor)
        {
            foreach (var item in preprocessor.Items)
                ApplyNamespaceMetadata(item, namespaceIdentity, namespaceScopeId);
            if (preprocessor.ElseBranch != null)
                ApplyNamespaceMetadata(preprocessor.ElseBranch, namespaceIdentity, namespaceScopeId);
        }
    }

    private static bool ContainsNamespaceScopedDeclarations(
        TypePreprocessorBlockNode block)
        => block.Classes.Count > 0
           || block.Interfaces.Count > 0
           || block.Enums.Count > 0
           || block.Delegates.Count > 0
           || block.InteropBlocks.Any(interop =>
               !interop.CSharpCode.TrimStart().StartsWith(
                   "#",
                   StringComparison.Ordinal))
           || block.NestedBlocks.Any(ContainsNamespaceScopedDeclarations)
           || block.ElseBranch != null
           && ContainsNamespaceScopedDeclarations(block.ElseBranch);

    private static void ApplyTypeIdentity(
        TypeDefinitionNode type,
        string namespaceIdentity,
        string namespaceScopeId,
        string? containingIdentity)
    {
        type.NamespaceIdentity = namespaceIdentity;
        type.NamespaceScopeId = namespaceScopeId;
        var arity = type switch
        {
            ClassDefinitionNode cls => cls.TypeParameters.Count,
            InterfaceDefinitionNode iface => iface.TypeParameters.Count,
            _ => 0
        };
        var suffix = arity > 0 ? $"`{arity}" : "";
        var identity = containingIdentity != null
            ? $"{containingIdentity}+{type.Name}{suffix}"
            : $"global::{(string.IsNullOrEmpty(namespaceIdentity) ? "" : namespaceIdentity + ".")}{type.Name}{suffix}";
        type.FullyQualifiedSymbolIdentity = identity;

        if (type is not ClassDefinitionNode classNode)
            return;
        foreach (var nested in classNode.NestedClasses)
            ApplyTypeIdentity(nested, namespaceIdentity, namespaceScopeId, identity);
        foreach (var nested in classNode.NestedInterfaces)
            ApplyTypeIdentity(nested, namespaceIdentity, namespaceScopeId, identity);
        foreach (var nested in classNode.NestedEnums)
            ApplyTypeIdentity(nested, namespaceIdentity, namespaceScopeId, identity);
        foreach (var nested in classNode.NestedDelegates)
            ApplyTypeIdentity(nested, namespaceIdentity, namespaceScopeId, identity);
    }

    /// <summary>
    /// Parses a using directive.
    /// §U[System.Collections.Generic]            // using System.Collections.Generic;
    /// §U[Gen:System.Collections.Generic]        // using Gen = System.Collections.Generic;
    /// §U[static:System.Math]                    // using static System.Math;
    /// §U[global:System.Text]                    // global using System.Text;
    /// §U[global:static:System.Math]              // global using static System.Math;
    /// §U[global:Gen:System.Collections.Generic]  // global using Gen = System.Collections.Generic;
    /// </summary>
    private UsingDirectiveNode ParseUsingDirective()
    {
        var globalPrefix = Current.Text == "global" && Peek(1).Kind == TokenKind.Using;
        if (globalPrefix) Advance();
        var startToken = Expect(TokenKind.Using);
        var isGlobalUsing = globalPrefix;
        var isStaticUsing = Current.Span.Line == startToken.Span.Line && Current.Text == "static";
        if (isStaticUsing) Advance();
        string? nativeAlias = null;
        if (Current.Span.Line == startToken.Span.Line && Check(TokenKind.Identifier)
            && Peek(1).Kind == TokenKind.Equals)
        {
            var aliasToken = Advance();
            nativeAlias = aliasToken.Value as string ?? aliasToken.Text;
            Advance();
        }
        if (Current.Span.Line != startToken.Span.Line)
        {
            _diagnostics.ReportError(startToken.Span, DiagnosticCode.UnexpectedToken,
                "A using directive requires its namespace or alias target on the same line.");
            return new UsingDirectiveNode(startToken.Span, "", nativeAlias, isStaticUsing, isGlobalUsing);
        }
        var target = ReadInlineTypeToken();
        // A using on the last line without a trailing newline is followed by the
        // closing dedents on that same line; they are not trailing tokens.
        if (target.Length == 0 || Current.Span.Line == startToken.Span.Line && !IsAtEnd
                && !Check(TokenKind.Dedent))
            _diagnostics.ReportError(startToken.Span, DiagnosticCode.UnexpectedToken,
                "Use 'using Namespace', 'using static Type', or 'using Alias = Target'.");
        return new UsingDirectiveNode(startToken.Span.Union(PreviousToken.Span),
            target, nativeAlias, isStaticUsing, isGlobalUsing);
    }

    private FunctionNode ParseFunction()
    {
        var startToken = Expect(TokenKind.Func);
        _lastIfToken = null; // scope Calor0117 fixes to this function's §IFs
        var attrs = ParseDeclarationAttributes(startToken);
        ValidateIdentityFreeHeader(startToken, attrs, 2);

        var id = GenerateParserAutoId("f");
        var funcName = attrs["_pos0"] ?? "";
        var visibilityStr = attrs["_pos1"] ?? "";
        var functionNameKey = "_pos0";

        if (string.IsNullOrEmpty(funcName))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "FUNC", "name");
            funcName = "";
        }
        // NEW: Parse optional type parameters §F{...}<T, U>
        var effectParameters = new List<EffectParameterInfo>();
        var typeParameters = ParseOptionalTypeParameterList(
            startToken.Span,
            effectParameters: effectParameters);
        ConsumeCanonicalDeclarationModifiers(startToken, attrs);
        if (attrs["_pos2"] is { } functionModifiers)
        {
            foreach (var modifier in functionModifiers.Split(','))
                if (modifier is not ("async" or "static"))
                    _diagnostics.ReportError(attrs.GetSpan("_pos2") ?? startToken.Span, DiagnosticCode.InvalidModifier,
                        $"Unsupported module-level function modifier '{modifier}'.");
        }
        var visibility = ParseVisibility(attrs["_pos1"], startToken.Span);
        // §7.3 position 1: this declaration binds its own effect variables, and they
        // stay in scope for its own row and its parameters' rows until it is parsed out.
        using var effectScope = PushEffectVariableScope(effectParameters);

        // --- Compact syntax: inline signature ---
        var parameters = new List<ParameterNode>();
        OutputNode? output = null;
        TryParseInlineSignature(startToken.Span, parameters, ref output);
        EffectsNode? effects = null;
        var preconditions = new List<RequiresNode>();
        var postconditions = new List<EnsuresNode>();
        var body = new List<StatementNode>();

        // Extended Features: Function-level metadata
        var examples = new List<ExampleNode>();
        var issues = new List<IssueNode>();
        UsesNode? uses = null;
        UsedByNode? usedBy = null;
        var assumptions = new List<AssumeNode>();
        ComplexityNode? complexity = null;
        var (since, deprecated, breakingChanges) = ParseDocumentationMetadata(startToken);
        var properties = new List<PropertyTestNode>();
        LockNode? lockNode = null;
        AuthorNode? author = null;
        TaskRefNode? taskRef = null;

        // Parse declaration sections before the body
        while (!IsAtEnd && !IsBlockEnd(TokenKind.EndFunc))
        {
            if (Check(TokenKind.Where))
            {
                // WHERE clauses add constraints to existing type parameters
                ParseWhereClause(typeParameters);
            }
            else if (Check(TokenKind.In))
            {
                parameters.Add(ParseParameter());
            }
            else if (Check(TokenKind.Out))
            {
                output = ParseOutput();
            }
            else if (Check(TokenKind.Exclamation))
            {
                effects = ParseEffects();
            }
            else if (Check(TokenKind.Requires))
            {
                preconditions.Add(ParseRequires());
            }
            else if (Check(TokenKind.FatArrow))
            {
                postconditions.Add(ParseEnsures());
            }
            // Extended Features: Function-level metadata
            else if (Check(TokenKind.Assume))
            {
                assumptions.Add(ParseAssume());
            }
            else
            {
                break;
            }
        }

        // Parse the indentation-delimited body.
        // #1485: an empty implicit body must not swallow a same-column sibling.
        var bodyOwner = BeginBlockBody(startToken, TokenKind.EndFunc, id);
        if (!IsBlockEnd(TokenKind.EndFunc) && IsInDeclarationBody(bodyOwner, interopIsSibling: false))
        {
            // Implicit body - parse statements until §/F
            body = ParseImplicitBody(bodyOwner);
        }

        var endToken = ExpectOwnedBlockEnd(TokenKind.EndFunc, bodyOwner);
        var endAttrs = ParseAttributes();

        // Validate ID matching

        var span = startToken.Span.Union(endToken.Span);
        var identifierSpan = GetIdentifierSpan(attrs, functionNameKey, funcName);
        return WithDocumentation(new FunctionNode(span, id, funcName, visibility, typeParameters, parameters, output, effects,
            preconditions, postconditions, body, attrs,
            examples, issues, uses, usedBy, assumptions, complexity, since, deprecated, breakingChanges,
            properties, lockNode, author, taskRef,
            isAsync: (attrs["_pos2"] ?? "").Split(',').Contains("async"),
            identifierSpan: identifierSpan)
        {
            EffectParameters = effectParameters,
        }, startToken);
    }

    private ParameterNode ParseParameter()
    {
        var startToken = Expect(TokenKind.In);
        var attrs = ParseAttributes();

        var (typeName, paramName, semantic) = AttributeHelper.InterpretInputAttributes(attrs);
        if (string.IsNullOrEmpty(paramName))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "IN", "name");
            paramName = "";
        }
        if (string.IsNullOrEmpty(typeName))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "IN", "type");
            typeName = "";
        }

        // Add semantic to attrs if present
        if (!string.IsNullOrEmpty(semantic))
        {
            attrs.Add("semantic", semantic);
        }

        // Parse parameter modifier from semantic/pos2 position (this, ref, out, in, params)
        var modifier = ParameterModifier.None;
        if (!string.IsNullOrEmpty(semantic))
        {
            modifier = ParseParameterModifiers(semantic, startToken.Span, allowSemantic: true);
        }

        var csharpAttrs = ParseCSharpAttributes();

        // Position 4 (§3.3): §I{Func<i32,i32>:f} §E{cw} — the row must sit on the same
        // line as the tag it annotates. A §E on a later line is left for the enclosing
        // §F/§MT/§DEL section loop's own §E arm, where it is the declaration's row.
        // Covers all 9 §I dispatch arms, because the check lives in the shared production.
        var row = TryParseSameLineRow(EffectRowPosition.Parameter);

        // Parse optional inline refinement: §I{type:name | (predicate using #)}
        InlineRefinementInfo? inlineRefinement = null;
        if (Check(TokenKind.Pipe))
        {
            Advance(); // consume '|'
            var saved = _insideRefinementPredicate;
            _insideRefinementPredicate = true;
            try
            {
                var predicate = ParseExpression();
                inlineRefinement = new InlineRefinementInfo(typeName, predicate);
            }
            finally
            {
                _insideRefinementPredicate = saved;
            }
        }

        // Parse optional default value: §I{type:name} = expression
        ExpressionNode? defaultValue = null;
        if (Check(TokenKind.Equals))
        {
            Advance(); // consume '='
            defaultValue = ParseExpression();
        }

        var identifierSpan = GetIdentifierSpan(attrs, "_pos1", paramName);
        return new ParameterNode(
            startToken.Span,
            paramName,
            typeName,
            modifier,
            attrs,
            csharpAttrs,
            defaultValue,
            inlineRefinement,
            identifierSpan,
            attrs.GetSpan("_pos0"),
            row);
    }

    private OutputNode ParseOutput()
    {
        var startToken = Expect(TokenKind.Out);
        var attrs = ParseAttributes();

        // Interpret positional attributes
        var typeName = AttributeHelper.InterpretOutputAttributes(attrs);
        if (string.IsNullOrEmpty(typeName))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "OUT", "type");
            typeName = "";
        }

        // Position 6, §O spelling (§3.3): §O{Func<i32>} §E{cw}. Same line rule; a §E on
        // a later line is the declaration's row and is the canonical two-line corpus
        // form (54 occurrences across 23 files), so it must NOT be consumed here.
        // Covers all 7 §O dispatch arms.
        var row = TryParseSameLineRow(EffectRowPosition.Return);

        return new OutputNode(startToken.Span, typeName, attrs.GetSpan("_pos0"), row);
    }

    /// <summary>
    /// Where a <c>§E{…}</c> is being read. Governs one thing only in this slice:
    /// whether an in-scope effect variable may be <i>mentioned</i> here
    /// (design-doc §7.3's partition of the eight positions).
    /// </summary>
    private enum EffectRowPosition
    {
        /// <summary>Position 1 — the declaration's own row. Variables permitted.</summary>
        Declaration,
        /// <summary>Positions 4 and 5 — a parameter's row. Variables permitted.</summary>
        Parameter,
        /// <summary>Position 6 — a return row. Forbidden: it would be rank-2.</summary>
        Return,
        /// <summary>Position 7 — a binding's row. Nothing binds a variable there.</summary>
        Binding,
        /// <summary>Position 8 — a field's row. Nothing binds a variable there.</summary>
        Field,
        /// <summary>v0.17 S1 / #1136 — a property's row, the field position's
        /// twin. Nothing binds a variable there either.</summary>
        Property,
        /// <summary>Position 2 — a lambda literal has no type-parameter list (Z8b).</summary>
        Lambda,
        /// <summary>Position 3 — a delegate declaration has no type-parameter list (Z8).</summary>
        Delegate,
    }

    private static bool PermitsEffectVariables(EffectRowPosition position)
        => position is EffectRowPosition.Declaration or EffectRowPosition.Parameter;

    private static string DescribeEffectRowPosition(EffectRowPosition position) => position switch
    {
        EffectRowPosition.Return => "a return row — a returned function mentioning the caller's effect variable would be rank-2 polymorphism",
        EffectRowPosition.Binding => "a binding's row — a let or var binds no effect variable",
        EffectRowPosition.Field => "a field's row — a field binds no effect variable",
        EffectRowPosition.Property => "a property's row — a property binds no effect variable",
        EffectRowPosition.Lambda => "a lambda literal's row — a lambda has no type-parameter list to bind one",
        EffectRowPosition.Delegate => "a delegate declaration's row — a delegate has no type-parameter list to bind one",
        _ => "this position",
    };

    /// <summary>
    /// True when <paramref name="name"/> is bound as an effect variable by some
    /// enclosing declaration. Effect variables are ordinary identifiers, so the
    /// comparison is case-sensitive — unlike the effect taxonomy's lookup.
    /// </summary>
    private bool IsEffectVariableInScope(string name) => ResolveEffectVariable(name) >= 0;

    /// <summary>
    /// v0.15 E3 slice b, design-doc §7.4 — the INDEX of <paramref name="name"/>
    /// within the innermost enclosing declaration's <c>eff</c> list, or
    /// <c>-1</c> when nothing binds it. Slice a asked only "is it bound?" and
    /// threw the position away; site 6 needs the position, because that is the
    /// identity two declarations' binders are compared on
    /// (<c>eff e</c> on an interface member ≡ <c>eff f</c> on its
    /// implementation — both index 0).
    /// </summary>
    private int ResolveEffectVariable(string name)
    {
        for (var i = _effectVariableScopes.Count - 1; i >= 0; i--)
        {
            var binders = _effectVariableScopes[i];
            for (var index = 0; index < binders.Count; index++)
            {
                if (string.Equals(binders[index].Name, name, StringComparison.Ordinal))
                    return index;
            }
        }

        return -1;
    }

    internal EffectsNode ParseNativeEffectsRow()
    {
        if (!Check(TokenKind.Exclamation))
            _diagnostics.ReportUnexpectedToken(Current.Span, "'!' effect row", Current.Kind);
        return ParseEffects();
    }

    private EffectsNode ParseEffects(EffectRowPosition position = EffectRowPosition.Declaration)
    {
        var canonical = Check(TokenKind.Exclamation);
        var startToken = canonical ? Advance() : Expect(TokenKind.Effects);
        var attrs = canonical ? new AttributeCollection() : ParseAttributes();
        if (canonical)
            ReadNativeEffectAttributes(startToken, attrs, position);

        // A scoped variable named unknown retains its binder identity.
        if (attrs["_pos0"] == "unknown"
            && attrs.All().All(attribute => attribute.Key is "_pos0" or "_posCount")
            && !IsEffectVariableInScope("unknown"))
        {
            // 0.30 (maintainer decision 2026-11-14): a callable may declare ! unknown on
            // itself. Its body may then call APIs without effect contracts, and every
            // caller is charged unknown. Value rows (bindings, fields, properties) and
            // lambda literals still require known effects.
            if (position is not (EffectRowPosition.Declaration or EffectRowPosition.Parameter
                    or EffectRowPosition.Return or EffectRowPosition.Delegate))
                _diagnostics.ReportError(startToken.Span, DiagnosticCode.EffectRowMisplaced,
                    "! unknown is permitted only on a callable's own declaration and on callback "
                    + "parameter, return and delegate contracts.");
            return new EffectsNode(startToken.Span, new Dictionary<string, string> { ["unknown"] = "*" });
        }

        var variables = new List<string>();

        // Interpret positional attributes
        var effects = AttributeHelper.InterpretEffectsAttributes(
            attrs,
            code => _diagnostics.Report(
                startToken.Span,
                DiagnosticCode.UnknownEffectCode,
                $"Unknown effect code '{code}'. Use a code from the authoritative effect taxonomy."),
            IsEffectVariableInScope,
            variables,
            codesAreComplete: canonical);

        if (variables.Count > 0 && !PermitsEffectVariables(position))
        {
            // §7.3: the variable is bound, but not usable here.
            foreach (var variable in variables)
            {
                _diagnostics.ReportError(
                    startToken.Span,
                    DiagnosticCode.EffectVariableScope,
                    $"Effect variable '{variable}' cannot be used in {DescribeEffectRowPosition(position)}. "
                    + "An effect variable may appear only in the row of the declaration that binds it "
                    + "and in the rows of that declaration's parameters.");
            }

            variables.Clear();
        }

        // §7.4 — persist the binder POSITION, not just the name. Resolved here
        // rather than inside InterpretEffectsAttributes so the attribute helper
        // keeps its boolean predicate and gains no knowledge of scopes.
        var ordinals = new List<int>(variables.Count);
        foreach (var variable in variables)
            ordinals.Add(ResolveEffectVariable(variable));

        return new EffectsNode(startToken.Span, effects, variables, ordinals);
    }

    private void ReadNativeEffectAttributes(Token startToken, AttributeCollection attrs, EffectRowPosition position)
    {
        var index = 0;
        while (!IsAtEnd && Current.Span.Line == startToken.Span.Line
               && (Check(TokenKind.Identifier) || Current.IsKeyword || Check(TokenKind.Star)
                   || Check(TokenKind.Exclamation)))
        {
            var effect = Advance();
            var code = effect.Text;
            if (effect.Kind == TokenKind.Exclamation && effect.Span.End == Current.Span.Start
                && Current.Span.Line == startToken.Span.Line
                && (Check(TokenKind.Identifier) || Current.IsKeyword))
                code += Advance().Text;
            while (Current.Span.Line == startToken.Span.Line && Check(TokenKind.Colon))
            {
                var colon = Advance();
                if (Current.Span.Line != startToken.Span.Line
                    || !(Check(TokenKind.Identifier) || Current.IsKeyword))
                {
                    _diagnostics.ReportUnexpectedToken(colon.Span,
                        "an effect code after ':' on the same line", Current.Kind);
                    return;
                }
                code += ":" + Advance().Text;
            }
            attrs.Add($"_pos{index++}", code, effect.Span);
            if (position == EffectRowPosition.Parameter && Check(TokenKind.Comma)
                && NativeRowCommaStartsParameter())
                break;
            if (Current.Span.Line != startToken.Span.Line || !Match(TokenKind.Comma)) break;
        }
    }

    private bool NativeRowCommaStartsParameter()
    {
        var type = new System.Text.StringBuilder();
        var depth = 0;
        for (var offset = 1; Peek(offset).Kind != TokenKind.Eof; offset++)
        {
            var token = Peek(offset);
            if (token.Span.Line != Current.Span.Line) break;
            if (depth == 0 && token.Kind is TokenKind.Comma or TokenKind.CloseParen)
                break;
            if (depth == 0 && token.Kind == TokenKind.Colon)
            {
                if (Peek(offset + 1).Kind == TokenKind.Colon)
                {
                    type.Append("::");
                    offset++;
                    continue;
                }
                return !EffectCodes.IsKnownCompactCode($"{type}:{Peek(offset + 1).Text}");
            }
            if (token.Kind is TokenKind.Less or TokenKind.OpenBracket or TokenKind.OpenParen) depth++;
            else if (token.Kind is TokenKind.Greater or TokenKind.CloseBracket or TokenKind.CloseParen) depth--;
            else if (token.Kind == TokenKind.GreaterGreater) depth -= 2;
            type.Append(token.Text);
        }
        return false;
    }

    /// <summary>
    /// The token most recently consumed, or the current token when nothing has been
    /// consumed yet. <see cref="Peek"/> indexes without a lower bound, so this is the
    /// safe spelling of <c>Peek(-1)</c>.
    /// </summary>
    private Token PreviousToken => _position > 0 ? _tokens[_position - 1] : Current;

    /// <summary>
    /// Reads a <c>§E{…}</c> as the effect row of the type just consumed, if and only
    /// if it is <b>same-line adjacent</b> to that type — design-doc §3 Decision 1.
    /// A <c>§E</c> on a later line is deliberately left unconsumed: at a position
    /// whose enclosing section loop has a <c>§E</c> arm it is the declaration's own
    /// row (the 2948/471-occurrence two-line arrow corpus depends on this), and at a
    /// position with no such arm the enclosing loop's recovery reports Calor0405.
    /// </summary>
    private EffectsNode? TryParseSameLineRow(EffectRowPosition position)
    {
        if (!Check(TokenKind.Exclamation))
            return null;

        if (Current.Span.Line != PreviousToken.Span.Line)
            return null;

        var row = ParseEffects(position);

        // A type carries at most ONE row. Without this, a second adjacent §E is not
        // merely unhelpful, it is silent and wrong: in `§I{str:m} §E{cw} §E{net}` the
        // first becomes the parameter's row and the second falls through to the §F
        // section loop's §E arm and becomes the DECLARATION's row — two different
        // meanings from one line, with no diagnostic. Inline, the same shape cascades.
        // A row mixing effects is written `§E{cw, net}`, which is what the message says.
        while (Check(TokenKind.Exclamation)
            && Current.Span.Line == PreviousToken.Span.Line)
        {
            _diagnostics.ReportError(
                Current.Span,
                DiagnosticCode.EffectRowMisplaced,
                "A type can carry only one effect row. Combine the effects into a single "
                + "row — ! cw, net — or, if this is meant to be the function's own effect "
                + "row, move it onto its own line at the top of the function body.");

            var startToken = Advance();
            if (startToken.Kind == TokenKind.Exclamation)
                ReadNativeEffectAttributes(startToken, new AttributeCollection(), position);
            else
                ParseAttributes();
        }

        return row;
    }

    /// <summary>
    /// Reports Calor0405 for a <c>§E{…}</c> that is not same-line adjacent to any type
    /// and sits at a position with no declaration-level <c>§E</c> arm to fall through
    /// to, then <b>consumes the whole group</b> so the rest of the enclosing production
    /// still parses. That recovery is what collapses the executed baselines from four
    /// (Z1) and eight (Z3) diagnostics to one.
    /// </summary>
    private void ReportMisplacedEffectRow(string? subject, string subjectKind)
    {
        var target = subject is { Length: > 0 }
            ? $"the end of the '{subject}' {subjectKind} line"
            : "the end of the line whose type it annotates";

        _diagnostics.ReportError(
            Current.Span,
            DiagnosticCode.EffectRowMisplaced,
            $"An effect row must be on the same line as the type it annotates. Move it onto {target}, "
            + "or — if this is meant to be the function's own effect row — onto its own line at the top of the function body.");

        // Consume §E and its attribute group(s) so the caller resumes cleanly.
        var startToken = Advance();
        if (startToken.Kind == TokenKind.Exclamation)
            ReadNativeEffectAttributes(startToken, new AttributeCollection(), EffectRowPosition.Declaration);
        else
            ParseAttributes();
    }

    private RequiresNode ParseRequires()
    {
        var startToken = Expect(TokenKind.Requires);
        var attrs = ParseAttributes();
        // v2 syntax: message is first positional §Q{"message"} (condition)
        var message = attrs["_pos0"];

        var condition = ParseExpression();

        var span = startToken.Span.Union(condition.Span);
        return new RequiresNode(span, condition, message, attrs);
    }

    private EnsuresNode ParseEnsures()
    {
        var canonical = Check(TokenKind.FatArrow);
        var startToken = canonical ? Advance() : Expect(TokenKind.Ensures);
        var attrs = canonical ? new AttributeCollection() : ParseAttributes();
        // v2 syntax: message is first positional §S{"message"} (condition)
        var message = attrs["_pos0"];

        var condition = ParseExpression();

        var span = startToken.Span.Union(condition.Span);
        return new EnsuresNode(span, condition, message, attrs);
    }

    /// <summary>
    /// Parses a proof obligation statement.
    /// §PROOF{id:description} (boolean-expression)
    /// </summary>
    private ProofObligationNode ParseProofObligation()
    {
        var startToken = Expect(TokenKind.Proof);
        var attrs = ParseAttributes();
        ValidateIdentityFreeHeader(startToken, attrs, 1);

        var id = GenerateParserAutoId("po");
        var description = string.IsNullOrEmpty(attrs["_pos0"]) ? null : attrs["_pos0"];


        var condition = ParseExpression();

        var span = startToken.Span.Union(condition.Span);
        return new ProofObligationNode(span, id, description, condition, attrs);
    }

    /// <summary>
    /// Parses # (self-reference) inside a refinement predicate.
    /// </summary>
    private ExpressionNode ParseSelfRef()
    {
        var token = Advance(); // consume #
        if (!_insideRefinementPredicate)
        {
            _diagnostics.Report(token.Span, DiagnosticCode.SelfRefOutsidePredicate,
                "Self-reference '#' can only be used inside a refinement type predicate");
        }
        return new SelfRefNode(token.Span);
    }

    /// <summary>
    /// Parses an implicit body (no §BODY markers) - statements until §/F
    /// </summary>
    private List<StatementNode> ParseImplicitBody(BlockOwner owner)
    {
        var statements = new List<StatementNode>();

        // #1485: a flat body (statements at the function's own column) still
        // ends at a sibling declaration such as the next §F.
        while (!IsAtEnd && !IsBlockEnd(TokenKind.EndFunc) && !IsBlockEnd(TokenKind.EndAsyncFunc)
               && IsInDeclarationBody(owner, interopIsSibling: false))
        {
            var statement = ParseStatement();
            if (statement != null)
            {
                statements.Add(statement);
            }
        }

        return statements;
    }

    private StatementNode? ParseStatement()
    {
        // Statement nesting (deep §IF chains etc.) recurses outside the
        // expression depth counter -- convert an imminent stack overflow into
        // a catchable exception rather than a process abort.
        System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack();

        if (Check(TokenKind.Identifier) && Peek(1).Kind == TokenKind.Colon
            && Peek(1).Span.Line == Current.Span.Line)
        {
            return ParseLabelStatement();
        }
        else if (Check(TokenKind.Func))
        {
            _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken,
                "Local func declarations are removed. Use a let binding with a lambda instead.");
            var enclosingIf = _lastIfToken;
            ParseFunction();
            _lastIfToken = enclosingIf;
            return null;
        }
        else if (Check(TokenKind.Return))
        {
            return ParseReturnStatement();
        }
        else if (Check(TokenKind.For))
        {
            return ParseForStatement();
        }
        else if (Check(TokenKind.While))
        {
            return ParseWhileStatement();
        }
        else if (Check(TokenKind.Do))
        {
            return ParseDoWhileStatement();
        }
        else if (Check(TokenKind.If))
        {
            return ParseIfStatement();
        }
        else if (Check(TokenKind.Let) || Check(TokenKind.Var))
        {
            return ParseCanonicalBinding();
        }
        else if (Check(TokenKind.Match))
        {
            return ParseMatchStatement();
        }
        else if (Check(TokenKind.Foreach))
        {
            return ParseForeachStatement();
        }
        else if (Check(TokenKind.Assign))
        {
            return ParseAssignmentStatement();
        }
        else if (Check(TokenKind.Try))
        {
            return ParseTryStatement();
        }
        else if (Check(TokenKind.Using))
        {
            return ParseUsingStatement();
        }
        else if (Check(TokenKind.Throw))
        {
            return ParseThrowStatement();
        }
        else if (Check(TokenKind.Break))
        {
            return ParseBreakStatement();
        }
        else if (Check(TokenKind.Continue))
        {
            return ParseContinueStatement();
        }
        else if (Check(TokenKind.Goto))
        {
            return ParseGotoStatement();
        }
        else if (Check(TokenKind.Yield))
        {
            return ParseYieldReturnStatement();
        }
        // Print aliases
        else if (Check(TokenKind.RawCSharp)
            || Check(TokenKind.CSharpInterop) && Current.Value is RawCSharpFrame)
        {
            return ParseRawCSharpStatement();
        }
        else if (Check(TokenKind.CompilerDirective))
        {
            return ParseCompilerDirective();
        }
        // Preprocessor conditional block
        else if (Check(TokenKind.Preprocessor))
        {
            return ParsePreprocessorDirective();
        }
        // Synchronization
        else if (Check(TokenKind.SyncBlock))
        {
            return ParseSyncBlock();
        }
        // Unsafe/Low-Level statements
        else if (Check(TokenKind.Unsafe))
        {
            return ParseUnsafeBlock();
        }
        else if (Check(TokenKind.Fixed))
        {
            return ParseFixedStatement();
        }
        // Proof obligations
        else if (Check(TokenKind.Proof))
        {
            return ParseProofObligation();
        }
        else if (Check(TokenKind.Await))
        {
            var expr = ParseAwaitExpression();
            return new ExpressionStatementNode(expr.Span, expr);
        }
        // Expression statement: bare lisp expression like (inc x), (post-dec y)
        else if (Check(TokenKind.OpenParen))
        {
            var expr = ParseExpression();
            if (expr is not (CallExpressionNode or ExpressionCallNode or NewExpressionNode))
                _diagnostics.ReportError(expr.Span, DiagnosticCode.UnexpectedToken,
                    "A native expression statement must be an invocation or object creation.");
            return new ExpressionStatementNode(expr.Span, expr);
        }
        else if (Check(TokenKind.Identifier) || Check(TokenKind.At)
            || Check(TokenKind.This) || Check(TokenKind.Base) || Check(TokenKind.New))
        {
            var expr = ParseExpression();
            if (expr is not (CallExpressionNode or ExpressionCallNode or NewExpressionNode))
                _diagnostics.ReportError(expr.Span, DiagnosticCode.UnexpectedToken,
                    "A native expression statement must be an invocation or object creation.");
            return new ExpressionStatementNode(expr.Span, expr);
        }
        // Stray §EI/§EL: the clause was dedented past its §IF, so the
        // if-chain closed before the clause was seen and it now sits in
        // statement position. Report with a re-indentation fix and recover.
        else if (Check(TokenKind.Else))
        {
            return ParseMisalignedElseClause();
        }
        _diagnostics.ReportUnexpectedToken(Current.Span, "statement", Current.Kind);
        Advance();
        return null;
    }

    /// <summary>
    /// Error recovery for a <c>§EI</c>/<c>§EL</c> clause encountered in
    /// statement position (Calor0117). The clause's indentation did not
    /// align with any open <c>§IF</c> -- almost always because it was
    /// dedented too far. Emits a machine-applicable fix that re-indents the
    /// clause line to the column of the most recently parsed <c>§IF</c>,
    /// then consumes the clause header so the following body parses as
    /// ordinary statements instead of cascading unexpected-token errors.
    ///
    /// Two guards keep the fix honest: <c>_lastIfToken</c> is reset at every
    /// function/method boundary, so a stray clause in a function with no
    /// <c>§IF</c> never gets a fix pointing at a different function's
    /// <c>§IF</c>; and a fix is only emitted when applying it would actually
    /// change the line -- a no-op fix (clause already at the target column)
    /// would send an agent apply→recheck loop into an infinite cycle.
    /// </summary>
    private StatementNode? ParseMisalignedElseClause()
    {
        var clauseToken = Advance(); // else if / else
        var tag = clauseToken.Kind == TokenKind.ElseIf ? "else if" : "else";
        var message = $"'{tag}' is not aligned with any open 'if' -- the if-chain already closed at a " +
            "shallower indent level. Indent the clause to the same column as its 'if'.";

        // The edit replaces the clause line's leading columns with spaces;
        // it is a no-op when the clause already sits at the target column.
        // (Leading tabs cannot make same-width leading text differ here:
        // token columns are computed after the lexer's tab handling, and
        // tab-indented lines are already covered by Calor0008/Calor0099.)
        if (_lastIfToken is { } ifTok
            && ifTok.Span.Column != clauseToken.Span.Column)
        {
            int targetIndent = Math.Max(0, ifTok.Span.Column - 1);
            int leadingLength = Math.Max(0, clauseToken.Span.Column - 1);
            var fix = new SuggestedFix(
                $"Re-indent '{tag}' to column {ifTok.Span.Column} (aligned with the 'if' on line {ifTok.Span.Line})",
                TextEdit.Replace(_diagnostics.CurrentFilePath ?? "", clauseToken.Span.Line, 1,
                    clauseToken.Span.Line, leadingLength + 1, new string(' ', targetIndent)));
            _diagnostics.ReportErrorWithFix(clauseToken.Span,
                DiagnosticCode.MisalignedElseClause, message, fix);
        }
        else
        {
            _diagnostics.ReportError(clauseToken.Span,
                DiagnosticCode.MisalignedElseClause, message);
        }

        // Consume the §EI condition so the clause body that follows parses
        // as ordinary statements.
        if (clauseToken.Kind == TokenKind.ElseIf && IsExpressionStart())
        {
            ParseExpression();
        }
        return null;
    }

    private ReturnStatementNode ParseReturnStatement()
    {
        var startToken = Expect(TokenKind.Return);

        ExpressionNode? expression = null;

        // Check if there's an expression following RETURN
        if (Current.Span.Line == TokenEndLine(startToken) && IsExpressionStart())
        {
            expression = ParseExpression();
        }

        var span = expression != null ? startToken.Span.Union(expression.Span) : startToken.Span;
        return new ReturnStatementNode(span, expression);
    }

    private bool IsExpressionStart()
    {
        return ExpressionParsers.ContainsKey(Current.Kind);
    }

    // Deterministic guard against stack exhaustion on adversarially nested
    // expressions: StackOverflowException is uncatchable in .NET, so without
    // a cap a sub-512 KB input of nested parens aborts the whole process
    // (SIGABRT) from any surface that parses untrusted source. Each nesting
    // level costs ~4 frames (ParseLispExpression → Core → ParseLispArgument
    // → ParseParenExpressionOrInlineLambda) whose Debug-build frames are
    // large (~3 KB+ per level measured in Debug); 128 keeps the worst case
    // comfortably inside a 1 MB thread-pool stack while remaining far beyond
    // real code, machine-generated prefix chains included.
    // EnsureSufficientExecutionStack backstops environments with tighter
    // stacks.
    private const int MaxExpressionDepth = 128;
    private int _expressionDepth;
    private bool _allowSignedMinimumLiteral;
    private bool _allowSpacedCall = true;

    private ExpressionNode ParseExpression(int minimumPrecedence = 0, bool? allowSpacedCall = null)
    {
        if (_expressionDepth >= MaxExpressionDepth)
            return SkipTooDeepExpression();

        // Backstop for recursion the expression counter doesn't see (deeply
        // nested statements, mixed forms): converts an imminent overflow
        // into a catchable InsufficientExecutionStackException.
        System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack();

        var previousSpacedCall = _allowSpacedCall;
        _allowSpacedCall = allowSpacedCall ?? previousSpacedCall;
        _expressionDepth++;
        try
        {
            return ParseExpressionCore(minimumPrecedence, _allowSpacedCall);
        }
        finally
        {
            _expressionDepth--;
            _allowSpacedCall = previousSpacedCall;
        }
    }

    /// <summary>
    /// Depth-cap recovery: report once, swallow the rest of the
    /// sub-expression (tokens until this nesting level's brackets balance,
    /// leaving the enclosing closer for the caller), return a dummy value.
    /// The frames above the cap unwind normally against the remaining
    /// closers.
    /// </summary>
    private ExpressionNode SkipTooDeepExpression()
    {
        var span = Current.Span;
        _diagnostics.ReportError(span, DiagnosticCode.ExpressionNestingTooDeep,
            $"Expression nesting exceeds the maximum depth of {MaxExpressionDepth}; the remainder of the expression was skipped");

        var depth = 0;
        while (!IsAtEnd)
        {
            var kind = Current.Kind;
            if (kind is TokenKind.OpenParen or TokenKind.OpenBrace or TokenKind.OpenBracket)
            {
                depth++;
            }
            else if (kind is TokenKind.CloseParen or TokenKind.CloseBrace or TokenKind.CloseBracket)
            {
                if (depth == 0)
                    break;
                depth--;
            }
            Advance();
        }

        return new IntLiteralNode(span, 0);
    }

    private ExpressionNode ParseExpressionCore(int minimumPrecedence, bool allowSpacedCall)
    {
        var parsedRange = Check(TokenKind.RangeOp);
        var expr = ExpressionParsers.TryGetValue(Current.Kind, out var parser)
            ? parser(this)
            : RecoverFromUnexpectedToken();

        // Handle trailing member access (e.g., (typeof X).Name, result.Property)
        expr = ParseTrailingMemberAccess(expr);
        expr = ParseNativePostfix(expr, allowSpacedCall);

        while (Current.Span.Line == TokenEndLine(PreviousToken))
        {
            if (_position != _quantifierRangeDelimiterPosition
                && minimumPrecedence <= 14 && Check(TokenKind.RangeOp))
            {
                var range = Advance();
                if (parsedRange || Check(TokenKind.RangeOp))
                    _diagnostics.ReportError(range.Span, DiagnosticCode.UnexpectedToken,
                        "Range operators cannot be chained without parentheses.");
                var end = Current.Span.Line == TokenEndLine(range) && IsExpressionStart()
                    ? ParseExpression(15) : null;
                expr = new RangeExpressionNode(expr.Span.Union(end?.Span ?? range.Span), expr, end);
                parsedRange = true;
                continue;
            }
            if (minimumPrecedence <= 13 && Check(TokenKind.With))
            {
                expr = ParseWithExpression(expr);
                continue;
            }
            if (minimumPrecedence <= 9 && Current.Text is "is" or "as")
            {
                var operation = Advance();
                var type = ParseLispTypeName(out var typeSpan);
                string? variable = null;
                if (operation.Text == "is" && Check(TokenKind.Identifier)
                    && Current.Span.Line == TokenEndLine(PreviousToken))
                    variable = TokenIdentifier(Advance());
                expr = operation.Text == "is"
                    ? new IsPatternNode(expr.Span.Union(PreviousToken.Span), expr, type, variable, typeSpan)
                    : new TypeOperationNode(expr.Span.Union(typeSpan), TypeOp.As, expr, type, typeSpan);
                continue;
            }
            if (Check(TokenKind.Question) && minimumPrecedence <= 1)
            {
                var question = Advance();
                var whenTrue = ParseInfixOperand(question, 0);
                var colon = Expect(TokenKind.Colon);
                var whenFalse = ParseInfixOperand(colon, 1);
                expr = new ConditionalExpressionNode(expr.Span.Union(whenFalse.Span),
                    expr, whenTrue, whenFalse);
                continue;
            }

            var precedence = InfixPrecedence(Current.Kind);
            if (precedence == 0 || precedence < minimumPrecedence)
                break;

            var op = Advance();
            if (op.Kind == TokenKind.Minus && Check(TokenKind.Minus)
                && op.Span.End == Current.Span.Start)
                _diagnostics.ReportError(op.Span.Union(Current.Span), DiagnosticCode.UnexpectedToken,
                    "Update operator '--' is removed. Use set with an explicit value instead, for example set x x - 1.");
            var right = ParseInfixOperand(op,
                op.Kind == TokenKind.NullCoalesce ? precedence : precedence + 1);
            expr = op.Kind == TokenKind.NullCoalesce
                ? new NullCoalesceNode(expr.Span.Union(right.Span), expr, right)
                : new BinaryOperationNode(expr.Span.Union(right.Span),
                    BinaryOperatorExtensions.FromString(op.Text)!.Value, expr, right);
        }

        return expr;
    }

    private ExpressionNode ParsePositionalExpression() => ParseExpression(13, allowSpacedCall: false);

    private ExpressionNode ParseNativePostfix(ExpressionNode expr, bool allowSpacedCall)
    {
        while (Current.Span.Line == TokenEndLine(PreviousToken))
        {
            IReadOnlyList<string>? typeArguments = null;
            if (Check(TokenKind.Less) && PreviousToken.Kind == TokenKind.Identifier
                && IsGenericTypeBracket(requireCall: true))
                typeArguments = ParseNativeCallTypeArguments();

            if (Check(TokenKind.OpenParen)
                && (allowSpacedCall || typeArguments is not null || Current.Span.Start == PreviousToken.Span.End))
            {
                var open = Advance();
                var arguments = new List<ExpressionNode>();
                var names = new List<string?>();
                var modifiers = new List<string?>();
                while (!IsAtEnd && !Check(TokenKind.CloseParen) && !Check(TokenKind.Dedent))
                {
                    var escapedName = Check(TokenKind.At) && Peek(1).Kind == TokenKind.Identifier
                        && Peek(2).Kind == TokenKind.Colon;
                    string? name = null;
                    if (escapedName || Check(TokenKind.Identifier) && Peek(1).Kind == TokenKind.Colon)
                    {
                        if (escapedName)
                            Advance();
                        name = TokenIdentifier(Advance());
                        Advance(); // :
                    }
                    string? modifier = null;
                    if (Check(TokenKind.Out) || Check(TokenKind.In)
                        || Check(TokenKind.Identifier) && Current.Text == "ref")
                        modifier = Advance().Text;
                    if (!IsExpressionStart())
                    {
                        _diagnostics.ReportUnexpectedToken(Current.Span, "call argument", Current.Kind);
                        break;
                    }
                    arguments.Add(ParseExpression(allowSpacedCall: true));
                    names.Add(name);
                    modifiers.Add(modifier);
                    if (!Match(TokenKind.Comma))
                        break;
                    if (Check(TokenKind.CloseParen))
                        _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken,
                            "A call argument is required after ','.");
                }
                var close = Expect(TokenKind.CloseParen);
                if (typeArguments is { Count: > 0 }
                    || names.Any(name => name is not null) || modifiers.Any(modifier => modifier is not null))
                    ReportUnsupportedConditionalPostfix(expr, open.Span);
                expr = SplitNativeMemberReference(expr);
                var span = expr.Span.Union(close.Span);
                expr = expr is ReferenceNode reference
                    ? new CallExpressionNode(span, reference.Name, arguments,
                        names.Any(name => name is not null) ? names : null,
                        modifiers.Any(modifier => modifier is not null) ? modifiers : null,
                        typeArguments, calleeSpan: _groupedReferenceSpans.GetValueOrDefault(reference, reference.Span))
                    : new ExpressionCallNode(span, expr, arguments,
                        names.Any(name => name is not null) ? names : null,
                        modifiers.Any(modifier => modifier is not null) ? modifiers : null, typeArguments);
            }
            else if (Check(TokenKind.OpenBracket))
            {
                var open = Advance();
                var indices = new List<ExpressionNode>();
                do
                {
                    if (!IsExpressionStart())
                    {
                        _diagnostics.ReportUnexpectedToken(Current.Span, "index expression", Current.Kind);
                        break;
                    }
                    indices.Add(ParseExpression(allowSpacedCall: true));
                } while (Match(TokenKind.Comma));
                var close = Expect(TokenKind.CloseBracket);
                ReportUnsupportedConditionalPostfix(expr, open.Span);
                expr = SplitNativeMemberReference(expr);
                var span = expr.Span.Union(close.Span);
                expr = indices.Count == 1
                    ? new ArrayAccessNode(span, expr, indices[0])
                    : new MultiDimArrayAccessNode(span, expr, indices);
            }
            else if (Check(TokenKind.Question) && Peek(1).Kind == TokenKind.OpenBracket)
            {
                _diagnostics.ReportError(Advance().Span, DiagnosticCode.UnexpectedToken,
                    "Native null-conditional indexing is not yet supported.");
            }
            else if (Check(TokenKind.Dot) || Check(TokenKind.NullConditional))
            {
                expr = ParseTrailingMemberAccess(expr);
            }
            else
            {
                break;
            }
        }
        return expr;
    }

    private IReadOnlyList<string> ParseNativeCallTypeArguments()
    {
        var start = Current.Span;
        var text = new System.Text.StringBuilder();
        var depth = 0;
        Token? previous = null;
        do
        {
            var token = Advance();
            if (previous is { Kind: TokenKind.Identifier } && token.Kind == TokenKind.Identifier)
                text.Append(' ');
            text.Append(token.Text);
            depth += token.Kind switch
            {
                TokenKind.Less => 1,
                TokenKind.Greater => -1,
                TokenKind.GreaterGreater => -2,
                _ => 0
            };
            previous = token;
        } while (!IsAtEnd && depth > 0);

        var parsed = SyntaxFactory.ParseName($"Call{text}");
        if (parsed is not GenericNameSyntax generic || generic.ContainsDiagnostics
            || generic.TypeArgumentList.Arguments.Any(argument => argument is OmittedTypeArgumentSyntax))
        {
            _diagnostics.ReportError(start.Union(PreviousToken.Span), DiagnosticCode.UnexpectedToken,
                "A generic invocation requires a nonempty C# type argument list.");
            return [];
        }
        return generic.TypeArgumentList.Arguments.Select(argument => argument.ToString()).ToArray();
    }

    private ExpressionNode SplitNativeMemberReference(ExpressionNode expr)
    {
        if (expr is not ReferenceNode reference || reference.Name.Contains('<')
            || !reference.Name.Contains('.'))
            return expr;

        var parts = reference.Name.Split('.');
        var identifierSpan = _groupedReferenceSpans.GetValueOrDefault(reference, reference.Span);
        var offset = 0;
        ExpressionNode result = parts[0] switch
        {
            "this" when !reference.IsVerbatim => new ThisExpressionNode(new TextSpan(identifierSpan.Start, parts[0].Length,
                identifierSpan.Line, identifierSpan.Column)),
            "base" when !reference.IsVerbatim => new BaseExpressionNode(new TextSpan(identifierSpan.Start, parts[0].Length,
                identifierSpan.Line, identifierSpan.Column)),
            _ => new ReferenceNode(new TextSpan(identifierSpan.Start, parts[0].Length,
                identifierSpan.Line, identifierSpan.Column), parts[0], reference.IsVerbatim)
        };
        for (var index = 1; index < parts.Length; index++)
        {
            var part = parts[index];
            offset += parts[index - 1].Length + 1;
            var memberSpan = new TextSpan(identifierSpan.Start + offset, part.Length,
                identifierSpan.Line, identifierSpan.Column + offset);
            result = new FieldAccessNode(result.Span.Union(memberSpan), result, part, memberSpan);
        }
        result.IncludeSpan(reference.Span);
        return result;
    }

    private void ReportUnsupportedConditionalPostfix(ExpressionNode expr, TextSpan span)
    {
        if (HasConditionalPostfixTarget(expr))
            _diagnostics.ReportError(span, DiagnosticCode.UnexpectedToken,
                "This native null-conditional postfix form is not yet supported.");
    }

    private static bool HasConditionalPostfixTarget(ExpressionNode expr) => expr switch
    {
        NullConditionalNode => true,
        FieldAccessNode field => HasConditionalPostfixTarget(field.Target),
        ArrayAccessNode array => HasConditionalPostfixTarget(array.Array),
        MultiDimArrayAccessNode array => HasConditionalPostfixTarget(array.Array),
        ExpressionCallNode call => HasConditionalPostfixTarget(call.TargetExpression),
        _ => false
    };

    private static int TokenEndLine(Token token)
    {
        var line = token.Span.Line;
        for (var i = 0; i < token.Text.Length; i++)
        {
            if (token.Text[i] == '\n'
                || token.Text[i] == '\r' && (i + 1 == token.Text.Length || token.Text[i + 1] != '\n'))
                line++;
        }
        return line;
    }

    private static int InfixPrecedence(TokenKind kind) => kind switch
    {
        TokenKind.NullCoalesce => 2,
        TokenKind.PipePipe => 3,
        TokenKind.AmpAmp => 4,
        TokenKind.Pipe => 5,
        TokenKind.Caret => 6,
        TokenKind.Amp => 7,
        TokenKind.EqualEqual or TokenKind.BangEqual => 8,
        TokenKind.Less or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual => 9,
        TokenKind.LessLess or TokenKind.GreaterGreater => 10,
        TokenKind.Plus or TokenKind.Minus => 11,
        TokenKind.Star or TokenKind.Slash or TokenKind.Percent => 12,
        _ => 0
    };

    private ExpressionNode ParseInfixOperand(Token op, int minimumPrecedence)
    {
        if (Current.Span.Line != op.Span.Line || !IsExpressionStart())
        {
            _diagnostics.ReportUnexpectedToken(Current.Span, $"operand after '{op.Text}'", Current.Kind);
            return new IntLiteralNode(op.Span, 0);
        }
        return ParseExpression(minimumPrecedence);
    }

    private ExpressionNode ParsePrefixUnary()
    {
        var op = Advance();
        if (op.Kind == TokenKind.Minus && Check(TokenKind.Minus)
            && op.Span.End == Current.Span.Start)
            _diagnostics.ReportError(op.Span.Union(Current.Span), DiagnosticCode.UnexpectedToken,
                "Update operator '--' is removed. Use set with an explicit value instead, for example set x x - 1.");
        var immediateInteger = Check(TokenKind.IntLiteral);
        var literalText = Current.Text;
        var savedMinimum = _allowSignedMinimumLiteral;
        _allowSignedMinimumLiteral = op.Kind == TokenKind.Minus && immediateInteger;
        ExpressionNode operand;
        try
        {
            operand = ParseInfixOperand(op, 15);
        }
        finally
        {
            _allowSignedMinimumLiteral = savedMinimum;
        }
        if (op.Kind == TokenKind.Minus && immediateInteger && operand is IntLiteralNode unsigned
            && unsigned.Signedness == IntegerLiteralSignedness.Unsigned
            && (literalText.Contains('U') || literalText.Contains('u')))
            _diagnostics.ReportUnsignedNegativeLiteral(op.Span.Union(operand.Span));
        if (op.Kind == TokenKind.Minus && immediateInteger && operand is IntLiteralNode overflowing
            && overflowing.Magnitude > 0x8000_0000_0000_0000UL)
            _diagnostics.ReportSignedIntegerLiteralOverflow(op.Span.Union(operand.Span));
        // Fold immediate signed literals as before, preserving signed minima and
        // floating/decimal negative zero without applying this rule to grouping.
        if (op.Kind == TokenKind.Minus && immediateInteger && operand is IntLiteralNode literal
            && literal.Sign == IntegerLiteralSign.Positive
            && !literalText.Contains(':') && !literalText.Contains('u') && !literalText.Contains('U')
            && literal.Magnitude <= 0x8000_0000_0000_0000UL)
        {
            var width = literalText.EndsWith('l') || literalText.EndsWith('L')
                || literal.Magnitude > 0x8000_0000UL || literal.Base == IntegerLiteralBase.Hexadecimal
                    && literal.Signedness == IntegerLiteralSignedness.Unsigned
                ? IntegerLiteralWidth.Bits64 : IntegerLiteralWidth.Bits32;
            return new IntLiteralNode(op.Span.Union(operand.Span), literal.Magnitude,
                IntegerLiteralSign.Negative, literal.Base, width, IntegerLiteralSignedness.Signed);
        }
        if (op.Kind == TokenKind.Minus && operand is FloatLiteralNode number && !literalText.Contains(':'))
            return new FloatLiteralNode(op.Span.Union(operand.Span), -number.Value, number.IsDecimal)
                { IsSingle = number.IsSingle };
        return new UnaryOperationNode(op.Span.Union(operand.Span), op.Kind switch
        {
            TokenKind.Minus => UnaryOperator.Negate,
            TokenKind.Exclamation => UnaryOperator.Not,
            _ => UnaryOperator.BitwiseNot
        }, operand);
    }

    /// <summary>
    /// Recovers from an unexpected token in expression context by emitting a diagnostic,
    /// skipping the token, and returning an error placeholder expression.
    /// </summary>
    private ExpressionNode RecoverFromUnexpectedToken()
    {
        var token = Current;
        _diagnostics.ReportUnexpectedToken(token.Span, "expression", token.Kind);
        Advance(); // skip the unexpected token
        return new IntLiteralNode(token.Span, 0); // placeholder
    }

    /// <summary>
    /// Parses a collection/array initializer: {elem1, elem2, ...}
    /// </summary>
    private ExpressionNode ParseCollectionInitializer()
    {
        var startToken = Expect(TokenKind.OpenBrace);
        var elements = new List<ExpressionNode>();

        // Parse elements until closing brace
        while (!IsAtEnd && !Check(TokenKind.CloseBrace))
        {
            var element = ParseExpression();
            elements.Add(element);

            // Elements are separated by commas
            if (Check(TokenKind.Comma))
            {
                Advance();
            }
            else if (!Check(TokenKind.CloseBrace))
            {
                // If there's no comma and no closing brace, something's wrong
                break;
            }
        }

        var endToken = Expect(TokenKind.CloseBrace);
        var span = startToken.Span.Union(endToken.Span);

        // Create an ArrayCreationNode with inferred type
        return new ArrayCreationNode(
            span,
            "arr_init",
            "arr_init",
            "any", // Type will be inferred by context
            null,  // No explicit size
            elements,
            new AttributeCollection());
    }

    /// <summary>
    /// Parses either a Lisp-style expression or an inline lambda.
    /// - Inline lambda: () → body or (param) → body
    /// - Lisp expression: (op args...)
    /// </summary>
    private ExpressionNode ParseParenExpressionOrInlineLambda()
    {
        if (TryParseNativeLambda(out var lambda))
            return lambda;
        if (TryParseNativeCast(out var cast))
            return cast;
        // Look ahead to determine if this is an inline lambda: () → or (params) →
        // We need to see if after the closing paren there's an arrow
        var savedPosition = _position;
        var startToken = Expect(TokenKind.OpenParen);

        // Check for empty parens followed by arrow: () →
        if (Check(TokenKind.CloseParen))
        {
            Advance(); // consume )
            if (Check(TokenKind.FatArrow) && Current.Span.Line == TokenEndLine(PreviousToken))
            {
                // This is an inline lambda with no parameters
                Advance(); // consume →
                var (exprBody, stmtBody, endSpan) = ParseLambdaBody();
                var span = startToken.Span.Union(endSpan);
                return new LambdaExpressionNode(
                    span,
                    "inline",
                    new List<LambdaParameterNode>(),
                    null, // effects
                    false, // not async
                    exprBody, // expressionBody
                    stmtBody, // statementBody
                    new AttributeCollection());
            }
            _diagnostics.ReportError(startToken.Span.Union(PreviousToken.Span),
                DiagnosticCode.UnexpectedToken, "Empty parentheses require an invocation target or a lambda body.");
            return new IntLiteralNode(startToken.Span, 0);
        }

        // Check for single parameter: (param) → or (type:param) →
        if (Check(TokenKind.Identifier))
        {
            var firstToken = Advance();
            var paramName = firstToken.Text;
            string? paramType = null;
            var parameterSpan = firstToken.Span;

            // Check for typed parameter: type:name
            if (Check(TokenKind.Colon))
            {
                Advance(); // consume :
                paramType = paramName;
                var nameToken = Expect(TokenKind.Identifier);
                paramName = nameToken.Text;
                parameterSpan = nameToken.Span;
            }

            if (Check(TokenKind.CloseParen))
            {
                Advance(); // consume )
                if (Check(TokenKind.FatArrow) && Current.Span.Line == TokenEndLine(PreviousToken))
                {
                    // This is an inline lambda with one parameter
                    Advance(); // consume →
                    var (exprBody, stmtBody, endSpan) = ParseLambdaBody();
                    var span = startToken.Span.Union(endSpan);
                    var param = new LambdaParameterNode(
                        firstToken.Span,
                        paramName,
                        paramType,
                        parameterSpan,
                        paramType == null ? null : firstToken.Span);
                    return new LambdaExpressionNode(
                        span,
                        "inline",
                        new List<LambdaParameterNode> { param },
                        null, // effects
                        false, // not async
                        exprBody, // expressionBody
                        stmtBody, // statementBody
                        new AttributeCollection());
                }
            }
            // Not a lambda, restore and parse as Lisp
            _position = savedPosition;
        }
        else
        {
            // Not an identifier, restore position (we already consumed the open paren)
            _position = savedPosition;
        }

        var open = Expect(TokenKind.OpenParen);
        var grouped = ParseExpression(allowSpacedCall: true);
        if (Match(TokenKind.Comma))
        {
            var elements = new List<ExpressionNode> { grouped };
            do
            {
                elements.Add(ParseExpression());
            } while (Match(TokenKind.Comma));
            var tupleClose = Expect(TokenKind.CloseParen);
            return new TupleLiteralNode(open.Span.Union(tupleClose.Span), elements);
        }
        var close = Expect(TokenKind.CloseParen);
        if (HasConditionalPostfixTarget(grouped) && Current.Span.Line == TokenEndLine(close)
            && Current.Kind is TokenKind.Dot or TokenKind.NullConditional or TokenKind.OpenParen or TokenKind.OpenBracket)
            _diagnostics.ReportError(close.Span, DiagnosticCode.UnexpectedToken,
                "A parenthesized null-conditional chain followed by another postfix operation is not yet supported.");
        if (grouped is ReferenceNode reference)
            _groupedReferenceSpans.TryAdd(reference, reference.Span);
        grouped.IncludeSpan(open.Span.Union(close.Span));
        return grouped;
    }

    private bool TryParseNativeLambda(out ExpressionNode expression)
    {
        expression = null!;
        if (!IsNativeLambdaAt(_position))
            return false;
        var open = Advance();
        var parameters = new List<LambdaParameterNode>();
        while (!IsAtEnd && !Check(TokenKind.CloseParen))
        {
            var start = Current.Span;
            var first = ReadInlineTypeToken();
            var firstSpan = start.Union(PreviousToken.Span);
            var nameToken = PreviousToken;
            var name = TokenIdentifier(nameToken);
            string? type = null;
            TextSpan? typeSpan = null;
            if (Match(TokenKind.Colon))
            {
                type = first;
                typeSpan = firstSpan;
                nameToken = Expect(TokenKind.Identifier);
                name = TokenIdentifier(nameToken);
            }
            else if (nameToken.Kind != TokenKind.Identifier || firstSpan != nameToken.Span)
            {
                _diagnostics.ReportError(firstSpan, DiagnosticCode.UnexpectedToken,
                    "A typed lambda parameter requires 'type:name'.");
            }
            parameters.Add(new LambdaParameterNode(start.Union(nameToken.Span),
                name, type, nameToken.Span, typeSpan));
            if (!Match(TokenKind.Comma))
                break;
        }
        Expect(TokenKind.CloseParen);
        var effects = Check(TokenKind.Exclamation) ? ParseEffects(EffectRowPosition.Lambda) : null;
        Expect(TokenKind.FatArrow);
        var (body, statements, end) = ParseLambdaBody();
        expression = new LambdaExpressionNode(open.Span.Union(end), "inline", parameters,
            effects, false, body, statements, new AttributeCollection());
        return true;
    }

    private bool IsNativeLambdaAt(int position)
    {
        if (position >= _tokens.Count || _tokens[position].Kind != TokenKind.OpenParen)
            return false;
        var depth = 0;
        var close = position;
        for (; close < _tokens.Count; close++)
        {
            if (_tokens[close].Kind == TokenKind.OpenParen)
                depth++;
            else if (_tokens[close].Kind == TokenKind.CloseParen && --depth == 0)
                break;
        }
        if (close + 1 >= _tokens.Count)
            return false;
        var arrow = close + 1;
        if (_tokens[arrow].Kind == TokenKind.Exclamation)
        {
            while (arrow < _tokens.Count
                   && _tokens[arrow].Span.Line == TokenEndLine(_tokens[close])
                   && (_tokens[arrow].Kind is TokenKind.Exclamation or TokenKind.Identifier
                       or TokenKind.Star or TokenKind.Colon or TokenKind.Comma
                       || _tokens[arrow].IsKeyword))
                arrow++;
        }
        return arrow < _tokens.Count && _tokens[arrow].Kind == TokenKind.FatArrow
            && _tokens[arrow].Span.Line == TokenEndLine(_tokens[close]);
    }

    private bool TryParseNativeCast(out ExpressionNode expression)
    {
        expression = null!;
        var depth = 0;
        var close = _position;
        for (; close < _tokens.Count; close++)
        {
            if (_tokens[close].Kind == TokenKind.OpenParen)
                depth++;
            else if (_tokens[close].Kind == TokenKind.CloseParen && --depth == 0)
                break;
        }
        if (close + 1 >= _tokens.Count)
            return false;
        var following = _tokens[close + 1];
        // Retained prefix dispatch does not make native ?? a valid cast operand.
        if (following.Kind is TokenKind.FatArrow or TokenKind.NullConditional or TokenKind.NullCoalesce
            || following.Span.Line != TokenEndLine(_tokens[close]))
            return false;
        // Type operators inside grouping must not fuse into a named cast type.
        if (_tokens.Skip(_position + 1).Take(close - _position - 1)
            .Any(token => token.Text is "as" or "is"))
            return false;
        var typeText = string.Concat(_tokens.Skip(_position + 1).Take(close - _position - 1)
            .Select(token => token.Text));
        var type = SyntaxFactory.ParseTypeName(typeText);
        if (type.ContainsDiagnostics
            || type.DescendantNodesAndSelf().OfType<ArrayTypeSyntax>()
                .Any(array => array.RankSpecifiers.Any(rank =>
                    rank.Sizes.Any(size => size is not OmittedArraySizeExpressionSyntax))))
            return false;
        var unambiguousType = type is PredefinedTypeSyntax or NullableTypeSyntax or ArrayTypeSyntax
            or PointerTypeSyntax or TupleTypeSyntax;
        var typedLambdaCast = (type is GenericNameSyntax
                or QualifiedNameSyntax { Right: GenericNameSyntax })
            && following.Kind == TokenKind.OpenParen && IsNativeLambdaAt(close + 2);
        if (!unambiguousType
            && (following.Kind is TokenKind.Minus or TokenKind.Plus or TokenKind.Exclamation or TokenKind.Tilde
                || following.Kind == TokenKind.OpenParen && !typedLambdaCast))
            return false;
        var saved = _position;
        _position = close + 1;
        var hasOperand = IsExpressionStart();
        _position = saved;
        if (!hasOperand)
            return false;
        var open = Advance();
        var typeStart = Current.Span;
        _position = close;
        var typeSpan = typeStart.Union(_tokens[close - 1].Span);
        Advance();
        var operand = ParseExpression(15);
        expression = new TypeOperationNode(open.Span.Union(operand.Span),
            TypeOp.Cast, operand, type.ToString(), typeSpan);
        return true;
    }

    private static string TokenIdentifier(Token token) => token.Value as string ?? token.Text.TrimStart('@');

    internal ExpressionNode ParseStandaloneExpression()
    {
        var expression = ParseExpression();
        while (Match(TokenKind.Dedent))
        {
        }
        if (!Check(TokenKind.Eof))
            _diagnostics.ReportUnexpectedToken(Current.Span, "end of expression", Current.Kind);
        return expression;
    }

    /// <summary>
    /// Parses a lambda body after the arrow (→).
    /// Can be either an expression or a statement block.
    /// Returns (expressionBody, statementBody, endSpan).
    /// </summary>
    private (ExpressionNode?, IReadOnlyList<StatementNode>?, TextSpan) ParseLambdaBody()
    {
        var arrow = PreviousToken;
        if (Current.Span.Line > TokenEndLine(arrow))
        {
            var owner = BeginBlockBody(arrow, TokenKind.EndLambda, null);
            var statements = new List<StatementNode>();
            while (!IsAtEnd && !Check(TokenKind.Dedent) && IsInBlockBody(owner)
                && Current.Kind is not (TokenKind.Comma or TokenKind.CloseParen
                    or TokenKind.CloseBracket or TokenKind.Colon or TokenKind.Semicolon))
            {
                var statement = ParseStatement();
                if (statement is not null)
                    statements.Add(statement);
            }
            if (statements.Count == 0)
                _diagnostics.ReportError(arrow.Span, DiagnosticCode.UnexpectedToken,
                    "A multiline lambda requires an indented statement body.");
            var end = Current.Kind is TokenKind.Comma or TokenKind.CloseParen
                or TokenKind.CloseBracket or TokenKind.Colon or TokenKind.Semicolon
                ? ImplicitBlockEndToken()
                : ExpectOwnedBlockEnd(TokenKind.EndLambda, owner);
            return (null, statements, end.Span);
        }

        var body = ParseExpression();
        return (body, null, body.Span);
    }

    /// <summary>
    /// Parses trailing member access chains: .Member, ?.Member, {index}
    /// Shared helper used by ParseLispArgument, ParseNewExpression, ParseCallExpression,
    /// ParseThisExpression, and ParseBaseExpression.
    /// </summary>
    private ExpressionNode ParseTrailingMemberAccess(ExpressionNode expr)
    {
        while (Check(TokenKind.Dot) || Check(TokenKind.NullConditional))
        {
            {
                var isNullConditional = Check(TokenKind.NullConditional);
                Advance(); // consume '.' or '?.'
                var escapedSpan = Check(TokenKind.At) ? Advance().Span : (TextSpan?)null;
                var memberToken = Expect(TokenKind.Identifier);
                var memberPath = TokenIdentifier(memberToken);
                var memberOffset = 0;
                foreach (var simpleMemberName in memberPath.Split('.'))
                {
                    var memberName = simpleMemberName;
                    var memberSpan = new TextSpan(memberToken.Span.Start + memberOffset, memberName.Length,
                        memberToken.Span.Line, memberToken.Span.Column + memberOffset);
                    if (memberName.Length == 0 && memberOffset == memberPath.Length
                        && (Check(TokenKind.At) || Check(TokenKind.Identifier) && Current.Text.StartsWith('@')))
                    {
                        var trailingEscape = Check(TokenKind.At) ? Advance().Span : (TextSpan?)null;
                        var escapedMember = Expect(TokenKind.Identifier);
                        memberName = TokenIdentifier(escapedMember);
                        memberSpan = trailingEscape?.Union(escapedMember.Span) ?? escapedMember.Span;
                    }
                    else if (memberOffset == 0 && escapedSpan is { } escaped)
                        memberSpan = escaped.Union(memberSpan);
                    else if (memberOffset == 0 && (memberName.Length == memberToken.Text.Length
                        || memberToken.Text.StartsWith('@')))
                        memberSpan = memberToken.Span;
                    if (memberOffset + simpleMemberName.Length == memberPath.Length
                        && Check(TokenKind.Less) && IsGenericTypeBracket() && !IsGenericTypeBracket(requireCall: true))
                    {
                        var name = new System.Text.StringBuilder(memberName);
                        name.Append('<');
                        Advance();
                        var depth = 1;
                        while (!IsAtEnd && depth > 0)
                        {
                            if (Check(TokenKind.Less)) { name.Append('<'); depth++; Advance(); }
                            else if (Check(TokenKind.Greater)) { name.Append('>'); depth--; Advance(); }
                            else if (Check(TokenKind.GreaterGreater)) { name.Append(">>"); depth -= 2; Advance(); }
                            else { name.Append(Current.Text); Advance(); }
                        }
                        memberName = name.ToString();
                    }
                    var span = expr.Span.Union(memberSpan);
                    expr = isNullConditional && memberOffset == 0
                        ? new NullConditionalNode(span, expr, memberName)
                        : new FieldAccessNode(span, expr, memberName, memberSpan);
                    memberOffset += simpleMemberName.Length + 1;
                }
            }
        }
        return expr;
    }

    /// <summary>
    /// Parses a type name in a Lisp expression context, handling qualified names and generics.
    /// Examples: int, string, List&lt;string&gt;, Dictionary&lt;string, int&gt;, System.Collections.Generic.List&lt;int&gt;
    /// Used by typeof, is, and as expressions.
    /// </summary>
    private string ParseLispTypeName() => ParseLispTypeName(out _);

    private string ParseLispTypeName(out TextSpan typeSpan)
    {
        var startPosition = _position;
        var startSpan = Current.Span;
        var typeName = ReadInlineTypeToken();
        typeSpan = _position > startPosition
            ? startSpan.Union(Peek(-1).Span)
            : TextSpan.Empty;
        return typeName;
    }

    /// <summary>
    /// Determines if a '<' token after an identifier is a generic type bracket (Type&lt;T&gt;)
    /// rather than a less-than operator. Uses lookahead to check if the token sequence
    /// matches a valid generic type pattern (identifiers, commas, dots, nested generics)
    /// terminated by matching '>'.
    /// </summary>
    private bool IsGenericTypeBracket(bool requireCall = false)
    {
        // Lookahead: scan tokens after '<' for a matching '>' that forms a valid generic
        var depth = 1;
        var i = _position + 1; // past the '<'
        while (i < _tokens.Count && depth > 0)
        {
            var kind = _tokens[i].Kind;
            if (kind == TokenKind.Less) depth++;
            else if (kind == TokenKind.Greater) depth--;
            else if (kind == TokenKind.GreaterGreater) depth -= 2;
            else if (kind == TokenKind.Identifier || kind == TokenKind.At || kind == TokenKind.Comma
                || kind == TokenKind.Question || kind == TokenKind.OpenBracket
                || kind == TokenKind.CloseBracket || kind == TokenKind.OpenParen
                || kind == TokenKind.CloseParen) { /* valid in generic args */ }
            else if (_tokens[i].Text == ".") { /* qualified name */ }
            else if (kind == TokenKind.Colon && i + 1 < _tokens.Count && _tokens[i + 1].Kind == TokenKind.Colon) { i++; /* :: */ }
            else return false; // not a valid generic type pattern
            i++;
        }
        if (requireCall)
            return depth == 0 && i < _tokens.Count && _tokens[i].Kind == TokenKind.OpenParen
                && _tokens[i].Span.Line == TokenEndLine(_tokens[i - 1]);
        return depth == 0 && i < _tokens.Count
            && (_tokens[i].Span.Line > TokenEndLine(_tokens[i - 1]) || _tokens[i].Kind is
            TokenKind.OpenParen or TokenKind.CloseParen or TokenKind.CloseBracket
            or TokenKind.CloseBrace or TokenKind.Colon or TokenKind.Comma or TokenKind.Dot
            or TokenKind.Question or TokenKind.EqualEqual or TokenKind.BangEqual
            or TokenKind.Pipe or TokenKind.Caret or TokenKind.Amp or TokenKind.AmpAmp
            or TokenKind.PipePipe or TokenKind.Eof or TokenKind.Dedent);
    }

    private IntLiteralNode ParseIntLiteral()
    {
        var token = Expect(TokenKind.IntLiteral);
        if (token.Value is IntLiteralInfo info)
        {
            if (info.Sign == IntegerLiteralSign.Positive
                && info.Signedness == IntegerLiteralSignedness.Signed
                && info.Magnitude == 0x8000_0000_0000_0000UL && !_allowSignedMinimumLiteral)
                _diagnostics.ReportSignedIntegerLiteralOverflow(token.Span);
            return new IntLiteralNode(
                token.Span,
                info.Magnitude,
                info.Sign,
                info.Base,
                info.Width,
                info.Signedness)
            {
                WidthInferred = info.WidthInferred,
            };
        }
        var value = token.Value switch
        {
            int i => (long)i,
            long l => l,
            _ => 0L
        };
        return new IntLiteralNode(token.Span, value);
    }

    private ExpressionNode ParseCharLiteral()
    {
        var token = Expect(TokenKind.CharLiteral);
        return new CharOperationNode(token.Span, CharOp.CharLiteral,
            [new StringLiteralNode(token.Span, ((char)token.Value!).ToString())]);
    }

    private ExpressionNode ParseStringLiteral()
    {
        var token = Expect(TokenKind.StrLiteral);
        if (token.Value is StringLiteralInfo interpolated)
        {
            var parts = new List<InterpolatedStringPartNode>(interpolated.Parts.Count);
            foreach (var part in interpolated.Parts)
            {
                switch (part)
                {
                    case InterpolatedStringTextTokenPart text:
                        parts.Add(new InterpolatedStringTextNode(token.Span, text.Text));
                        break;
                    case InterpolatedStringExpressionTokenPart expression:
                        parts.Add(ParseInlineInterpolationPart(expression, token.Span));
                        break;
                }
            }

            return new InterpolatedStringNode(token.Span, parts)
            {
                IsMultiline = interpolated.IsMultiline,
                IsUtf8 = interpolated.IsUtf8
            };
        }

        var value = token.Value as string ?? "";
        var isUtf8 = token.Text.EndsWith("u8");
        if (isUtf8 && value.EndsWith("u8"))
        {
            // Strip the u8 suffix from the value if the lexer included it
            value = value[..^2];
        }
        return new StringLiteralNode(token.Span, value)
        {
            IsMultiline = token.Text.StartsWith("\"\"\""),
            IsUtf8 = isUtf8
        };
    }

    private InterpolatedStringExpressionNode ParseInlineInterpolationPart(
        InterpolatedStringExpressionTokenPart part,
        TextSpan fallbackSpan)
    {
        var source = part.ExpressionText;
        // 0.25 F2 (#906): lex the hole at its real position, so a diagnostic inside it (an
        // undefined name, a type error) points at the hole instead of line 1, column 1.
        var holeSpan = new TextSpan(part.Start, source.Length, part.Line, part.Column);
        var lexer = new Lexer(source, _diagnostics, part.Start, part.Line, part.Column);
        var parser = new Parser(lexer.TokenizeAllForParser(), _diagnostics);
        var expression = parser.ParseExpression();
        var intent = part.Intent;

        var suffixStart = parser.Current.Kind == TokenKind.Eof
            ? source.Length
            : Math.Clamp(parser.Current.Span.Start - part.Start, 0, source.Length);
        var suffix = source[suffixStart..].Trim();
        string? alignment = null;
        string? format = null;

        if (suffix.Length > 0
            && !suffix.StartsWith(",", StringComparison.Ordinal)
            && !suffix.StartsWith(":", StringComparison.Ordinal))
        {
            _diagnostics.ReportError(holeSpan, DiagnosticCode.UnexpectedToken,
                $"Unexpected native interpolation suffix '{suffix}'.");
        }

        if (suffix.StartsWith(",", StringComparison.Ordinal))
        {
            var alignmentAndFormat = suffix[1..];
            var colon = alignmentAndFormat.IndexOf(':');
            if (colon >= 0)
            {
                alignment = alignmentAndFormat[..colon].Trim();
                format = alignmentAndFormat[(colon + 1)..];
            }
            else
            {
                alignment = alignmentAndFormat.Trim();
            }
        }
        else if (suffix.StartsWith(":", StringComparison.Ordinal))
        {
            format = suffix[1..];
        }
        else if (suffix.Length > 0)
        {
            _diagnostics.ReportError(
                fallbackSpan,
                DiagnosticCode.UnexpectedToken,
                $"Unexpected interpolation suffix '{suffix}'");
        }

        return new InterpolatedStringExpressionNode(
            holeSpan,
            expression,
            string.IsNullOrEmpty(format) ? null : format,
            string.IsNullOrEmpty(alignment) ? null : alignment,
            intent,
            source);
    }

    private BoolLiteralNode ParseBoolLiteral()
    {
        var token = Expect(TokenKind.BoolLiteral);
        var value = token.Value is bool b && b;
        return new BoolLiteralNode(token.Span, value);
    }

    private FloatLiteralNode ParseFloatLiteral()
    {
        var token = Expect(TokenKind.FloatLiteral);
        if (token.Value is FloatLiteralInfo info)
        {
            return new FloatLiteralNode(token.Span, info.Value) { IsSingle = info.IsSingle };
        }
        var value = token.Value is double d ? d : 0.0;
        return new FloatLiteralNode(token.Span, value);
    }

    private DecimalLiteralNode ParseDecimalLiteral()
    {
        var token = Expect(TokenKind.DecimalLiteral);
        var value = token.Value is decimal d ? d : 0m;
        return new DecimalLiteralNode(token.Span, value);
    }

    private ExpressionNode ParseVerbatimIdentifier()
    {
        var prefix = Advance();
        // The next token should be an identifier or keyword used as an identifier
        if (Check(TokenKind.Identifier) || Current.IsKeyword)
        {
            var expression = ParseReference(verbatim: true);
            if (expression is ReferenceNode reference)
                _groupedReferenceSpans.TryAdd(reference, reference.Span);
            expression.IncludeSpan(prefix.Span);
            return expression;
        }
        // Fallback: treat @ as ignored prefix
        return RecoverFromUnexpectedToken();
    }

    private bool IsNativeQuantifierHead()
    {
        if (Current.Text is not ("forall" or "exists"))
            return false;
        for (var colon = _position + 2; colon + 1 < _tokens.Count
             && _tokens[colon].Span.Line == Current.Span.Line; colon++)
        {
            if (_tokens[colon].Kind != TokenKind.Colon
                || _tokens[colon + 1].Kind != TokenKind.Identifier
                || _tokens[colon + 1].Span.Line != Current.Span.Line)
                continue;
            var type = string.Concat(_tokens.Skip(_position + 1).Take(colon - _position - 1)
                .Select(token => token.Text));
            if (!SyntaxFactory.ParseTypeName(type).ContainsDiagnostics)
                return true;
        }
        return false;
    }

    private ExpressionNode ParseNativeQuantifier()
    {
        var head = Advance();
        var variables = new List<QuantifierVariableNode>();
        ExpressionNode? guard = null;
        bool? ranged = null;
        Token ExpectOnHeadLine(TokenKind kind)
        {
            if (Current.Span.Line == head.Span.Line)
                return Expect(kind);
            _diagnostics.ReportUnexpectedToken(Current.Span, kind, Current.Kind);
            return new Token(kind, "", head.Span);
        }
        do
        {
            if (Current.Span.Line != head.Span.Line)
            {
                _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken,
                    "A quantified binding must stay on the quantifier's physical line.");
                break;
            }
            var typeStart = Current.Span;
            var type = ReadInlineTypeToken();
            var typeSpan = typeStart.Union(PreviousToken.Span);
            ExpectOnHeadLine(TokenKind.Colon);
            var name = ExpectOnHeadLine(TokenKind.Identifier);
            variables.Add(new QuantifierVariableNode(typeSpan.Union(name.Span),
                TokenIdentifier(name), type, name.Span, typeSpan));
            ranged ??= Check(TokenKind.In);
            if (ranged.Value)
            {
                var inToken = ExpectOnHeadLine(TokenKind.In);
                ExpressionNode lower;
                var previousDelimiter = _quantifierRangeDelimiterPosition;
                _quantifierRangeDelimiterPosition = FindQuantifierRangeDelimiter();
                try
                {
                    lower = ParseInfixOperand(inToken, 0);
                }
                finally
                {
                    _quantifierRangeDelimiterPosition = previousDelimiter;
                }
                var range = ExpectOnHeadLine(TokenKind.RangeOp);
                var upper = ParseInfixOperand(range, 0);
                var reference = new ReferenceNode(name.Span, TokenIdentifier(name), isVerbatim: true);
                var lowerTest = new BinaryOperationNode(name.Span.Union(lower.Span),
                    BinaryOperator.GreaterOrEqual, reference, lower);
                var upperTest = new BinaryOperationNode(name.Span.Union(upper.Span),
                    BinaryOperator.LessThan, reference, upper);
                var bounds = new BinaryOperationNode(lowerTest.Span.Union(upperTest.Span),
                    BinaryOperator.And, lowerTest, upperTest);
                guard = guard is null ? bounds
                    : new BinaryOperationNode(guard.Span.Union(bounds.Span), BinaryOperator.And, guard, bounds);
            }
        } while (Current.Span.Line == head.Span.Line && Match(TokenKind.Comma));
        if (ranged == false)
        {
            var where = head;
            if (Current.Text == "where" && Current.Span.Line == head.Span.Line)
                where = Advance();
            else
                _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken,
                    "A quantified binding requires 'in lower..upper' or 'where guard'.");
            guard = ParseInfixOperand(where, 0);
        }
        var colon = ExpectOnHeadLine(TokenKind.Colon);
        var predicate = ParseInfixOperand(colon, 0);
        var span = head.Span.Union(predicate.Span);
        return head.Text == "forall"
            ? new ForallExpressionNode(span, variables,
                new ImplicationExpressionNode(guard!.Span.Union(predicate.Span), guard, predicate))
            : new ExistsExpressionNode(span, variables,
                new BinaryOperationNode(guard!.Span.Union(predicate.Span), BinaryOperator.And, guard, predicate));
    }

    private ExpressionNode ParseReference(bool verbatim = false)
    {
        if (!verbatim && IsNativeQuantifierHead())
            return ParseNativeQuantifier();
        var token = Expect(TokenKind.Identifier);
        var name = TokenIdentifier(token);
        verbatim |= token.Text.StartsWith('@');
        if (!verbatim && name == "default" && Check(TokenKind.OpenParen)
            && Current.Span.Line == token.Span.Line)
        {
            Advance();
            var typeStart = _position;
            var type = ReadInlineTypeToken();
            var typeSpan = GetConsumedSpan(typeStart);
            var close = Expect(TokenKind.CloseParen);
            var span = token.Span.Union(close.Span);
            if (type == "bool")
                return new BoolLiteralNode(span, false);
            if (type is "string" or "object")
                return new TypeOperationNode(span, TypeOp.Cast, new ReferenceNode(span, "null"), type, typeSpan);
            if (type is "sbyte" or "byte" or "short" or "ushort" or "int" or "uint"
                or "long" or "ulong" or "nint" or "nuint" or "char" or "float" or "double" or "decimal")
                return new TypeOperationNode(span, TypeOp.Cast, new IntLiteralNode(span, 0), type, typeSpan);
            _diagnostics.ReportError(typeSpan, DiagnosticCode.UnexpectedToken,
                "Native default(T) currently requires a built-in primitive type.");
            return new IntLiteralNode(span, 0);
        }
        if (!verbatim && name is "static" or "async" && Check(TokenKind.OpenParen)
            && TryParseNativeLambda(out var parsedLambda) && parsedLambda is LambdaExpressionNode lambda)
            return new LambdaExpressionNode(token.Span.Union(lambda.Span), lambda.Id,
                lambda.Parameters, lambda.Effects, name == "async",
                lambda.ExpressionBody, lambda.StatementBody, lambda.Attributes, name == "static");
        if (!verbatim && name is "checked" or "unchecked")
        {
            Expect(TokenKind.OpenParen);
            var operand = ParseExpression();
            var close = Expect(TokenKind.CloseParen);
            return new OverflowContextExpressionNode(
                token.Span.Union(close.Span), operand, name == "checked");
        }
        if (!verbatim && name is "typeof" or "nameof" or "sizeof")
        {
            Expect(TokenKind.OpenParen);
            var value = ParseLispTypeName(out var typeSpan);
            var close = Expect(TokenKind.CloseParen);
            var span = token.Span.Union(close.Span);
            return name switch
            {
                "typeof" => new TypeOfExpressionNode(span, value, typeSpan),
                "sizeof" => new SizeOfNode(span, value),
                _ => new NameOfExpressionNode(span, value)
            };
        }

        if (name.EndsWith('.') && (Check(TokenKind.At)
            || Check(TokenKind.Identifier) && Current.Text.StartsWith('@')))
        {
            var escapedSpan = Check(TokenKind.At) ? Advance().Span : (TextSpan?)null;
            var member = Expect(TokenKind.Identifier);
            var receiverSpan = new TextSpan(token.Span.Start, token.Span.Length - 1,
                token.Span.Line, token.Span.Column);
            var reference = new ReferenceNode(receiverSpan, name[..^1], verbatim || token.Value is string);
            var receiver = name[..^1] switch
            {
                "this" when !verbatim => new ThisExpressionNode(receiverSpan),
                "base" when !verbatim => new BaseExpressionNode(receiverSpan),
                _ => SplitNativeMemberReference(reference)
            };
            return ParseTrailingMemberAccess(new FieldAccessNode(token.Span.Union(member.Span),
                receiver, TokenIdentifier(member), escapedSpan?.Union(member.Span) ?? member.Span));
        }

        // Handle generic type references: Type<T> or Type<T1, T2>
        // Invocation type arguments belong to the postfix parser; other brackets
        // still need the generic-reference versus comparison discriminator.
        if (Check(TokenKind.Less) && IsGenericTypeBracket() && !IsGenericTypeBracket(requireCall: true))
        {
            var sb = new System.Text.StringBuilder(name);
            sb.Append('<');
            Advance(); // consume <
            var depth = 1;
            while (!IsAtEnd && depth > 0)
            {
                if (Check(TokenKind.Less)) { sb.Append('<'); depth++; Advance(); }
                else if (Check(TokenKind.Greater)) { sb.Append('>'); depth--; Advance(); }
                else if (Check(TokenKind.GreaterGreater)) { sb.Append(">>"); depth -= 2; Advance(); }
                else { sb.Append(Current.Text); Advance(); }
            }
            name = sb.ToString();
        }

        ExpressionNode expr = new ReferenceNode(token.Span, name, verbatim || token.Value is string);
        if (name.Contains('.') && !name.Contains('<'))
        {
            if (_dottedValueReadStarts is null)
                _dottedReferenceTokenStarts[(ReferenceNode)expr] = token.Span.Start;
            else if (_dottedValueReadStarts.Contains(token.Span.Start))
                expr = SplitNativeMemberReference(expr);
        }

        return ParseTrailingMemberAccess(expr);
    }

    // Phase 3: Type System Expression Parsing

    private MatchExpressionNode ParseMatchExpression()
    {
        var startToken = Expect(TokenKind.Match);
        // #911 review F6: exactly one header group — a following brace group is a
        // collection-initializer expression, not more header attributes.
        var attrs = ParseAttributes(maxGroups: 1);
        ValidateIdentityFreeHeader(startToken, attrs, 0);
        var id = GenerateParserAutoId("w");

        var target = ParseExpression();
        var matchOwner = BeginBlockBody(startToken, TokenKind.EndMatch, id);
        var cases = ParseMatchCases(expressionContext: true);

        var endToken = ExpectOwnedBlockEnd(TokenKind.EndMatch, matchOwner);
        var endAttrs = ParseAttributes();


        var span = startToken.Span.Union(endToken.Span);
        return new MatchExpressionNode(span, id, target, cases, attrs);
    }

    private StatementNode ParseMatchStatement()
    {
        var startToken = Expect(TokenKind.Match);
        // #911 review F6: exactly one header group — a following brace group is a
        // collection-initializer expression, not more header attributes.
        var attrs = ParseAttributes(maxGroups: 1);
        ValidateIdentityFreeHeader(startToken, attrs, 0);
        var id = GenerateParserAutoId("w");

        if (attrs["_pos0"] == "expr")
        {
            var fix = new SuggestedFix(
                "Return the match expression explicitly with 'return'",
                TextEdit.Replace(_diagnostics.CurrentFilePath ?? "", startToken.Span.Line, startToken.Span.Column,
                    startToken.Span.Line, startToken.Span.Column, "return "));
            _diagnostics.ReportErrorWithFix(startToken.Span, DiagnosticCode.UnexpectedToken,
                "A statement-position match marked 'expr' is removed. Write 'return match ...' for a returned match expression.", fix);
        }

        var target = ParseExpression();
        var matchOwner = BeginBlockBody(startToken, TokenKind.EndMatch, id);
        var cases = ParseMatchCases(expressionContext: false);

        var endToken = ExpectOwnedBlockEnd(TokenKind.EndMatch, matchOwner);
        var endAttrs = ParseAttributes();


        var span = startToken.Span.Union(endToken.Span);

        return new MatchStatementNode(span, id, target, cases, attrs);
    }

    private List<MatchCaseNode> ParseMatchCases(bool expressionContext)
    {
        var cases = new List<MatchCaseNode>();
        var firstCaseColumn = -1;

        // #1485: a §K on a line shallower than this match's first case belongs
        // to an enclosing match (inside brackets the lexer emits no Dedent to
        // say so).
        while (Check(TokenKind.Case)
               && (firstCaseColumn < 0 || LineIndentColumnAt(_position) >= firstCaseColumn))
        {
            if (firstCaseColumn < 0) firstCaseColumn = LineIndentColumnAt(_position);
            var caseToken = Expect(TokenKind.Case);
            var pattern = ParsePattern();

            // Check for guard clause: §WHEN expression
            ExpressionNode? guard = null;
            if (Check(TokenKind.When))
            {
                Expect(TokenKind.When);
                guard = ParseExpression();
            }

            var body = new List<StatementNode>();

            // Check for arrow syntax: → expression (single expression case)
            if (Check(TokenKind.Arrow))
            {
                var arrow = Expect(TokenKind.Arrow);
                if (!expressionContext)
                    _diagnostics.ReportError(arrow.Span, DiagnosticCode.UnexpectedToken,
                        "Statement match arms require an indented body. Use 'case pattern' followed by indented statements, or 'return match' for a returned match expression.");
                var expr = ParseExpression();
                body.Add(new ReturnStatementNode(expr.Span, expr));
            }
            else if (IsNativeLambdaAt(_position))
            {
                _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken,
                    "A lambda match arm requires → before its expression. Use 'case pattern → (type:name) => expression'.");
                var expr = ParseExpression();
                body.Add(new ReturnStatementNode(expr.Span, expr));
                if (IsBlockEnd(TokenKind.EndCase)) ExpectBlockEnd(TokenKind.EndCase);
            }
            else
            {
                // Block syntax - parse statements until closing tag or next case.
                // #1485: the case owns only lines indented under its §K, so an
                // empty case never consumes the match's (or a parent's) Dedent.
                var caseOwner = BeginBlockBody(caseToken, TokenKind.EndCase, null, TokenKind.Case);
                while (!IsAtEnd && !Check(TokenKind.Case) && !IsBlockEnd(TokenKind.EndMatch) && !IsBlockEnd(TokenKind.EndCase)
                       && IsInBlockBody(caseOwner))
                {
                    var stmt = ParseStatement();
                    if (stmt != null)
                    {
                        body.Add(stmt);
                    }
                }

                // Consume optional §/K closing tag, or the case body's own Dedent
                if (IsOwnedDedent(caseOwner))
                {
                    ExpectBlockEnd(TokenKind.EndCase);
                }
            }

            var lastSpan = body.Count > 0 ? body[^1].Span : caseToken.Span;
            cases.Add(new MatchCaseNode(caseToken.Span.Union(lastSpan), pattern, guard, body));
        }

        return cases;
    }

    private ListPatternNode ParseNativeListPattern()
    {
        var open = Expect(TokenKind.OpenBracket);
        var patterns = new List<PatternNode>();
        VarPatternNode? slice = null;
        var sliceIndex = -1;
        while (!IsAtEnd && !Check(TokenKind.CloseBracket))
        {
            if (Check(TokenKind.RangeOp))
            {
                var range = Advance();
                if (slice != null)
                    _diagnostics.ReportError(range.Span, DiagnosticCode.UnexpectedToken,
                        "A list pattern may contain only one slice.");
                var rest = new VarPatternNode(range.Span, "_");
                if (Check(TokenKind.Var))
                {
                    Advance();
                    var name = Expect(TokenKind.Identifier);
                    rest = new VarPatternNode(range.Span.Union(name.Span),
                        TokenIdentifier(name), name.Span);
                }
                else if (!Check(TokenKind.Comma) && !Check(TokenKind.CloseBracket))
                {
                    _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken,
                        "A list slice must be '..' or '.. var name'.");
                }
                slice = rest;
                sliceIndex = patterns.Count;
            }
            else
            {
                patterns.Add(ParsePattern());
            }
            if (!Check(TokenKind.CloseBracket))
                Expect(TokenKind.Comma);
        }
        var close = Expect(TokenKind.CloseBracket);
        return new ListPatternNode(open.Span.Union(close.Span), patterns, slice, sliceIndex);
    }

    private PatternNode ParsePattern()
    {
        if (Check(TokenKind.OpenBracket))
            return ParseNativeListPattern();

        // Handle (not ...), (or ... ...), (and ... ...) pattern combinators
        if (Check(TokenKind.OpenParen))
        {
            var nextToken = Peek(1);
            if (nextToken.Kind == TokenKind.Identifier && nextToken.Text is "not" or "or" or "and")
            {
                var openParen = Expect(TokenKind.OpenParen);
                var keyword = Advance(); // consume not/or/and

                if (keyword.Text == "not")
                {
                    var inner = ParsePattern();
                    Expect(TokenKind.CloseParen);
                    return new NegatedPatternNode(openParen.Span.Union(inner.Span), inner);
                }
                else
                {
                    // Iterative parsing for deeply nested left-associative (or/and) patterns
                    // to avoid stack overflow (e.g., 1900+ nested (or ...) levels).
                    var combinator = keyword.Text;
                    int depth = 1;

                    while (Check(TokenKind.OpenParen) && Peek(1).Kind == TokenKind.Identifier
                           && Peek(1).Text == combinator)
                    {
                        Advance(); // consume (
                        Advance(); // consume or/and
                        depth++;
                    }

                    var result = ParsePattern(); // deepest left operand

                    for (int d = 0; d < depth; d++)
                    {
                        var right = ParsePattern();
                        Expect(TokenKind.CloseParen);
                        var span = openParen.Span.Union(right.Span);
                        result = combinator == "or"
                            ? new OrPatternNode(span, result, right)
                            : new AndPatternNode(span, result, right);
                    }

                    return result;
                }
            }
        }

        if (Check(TokenKind.OpenBrace))
            return ParseNativePropertyPattern();

        if (Check(TokenKind.Var))
        {
            var varToken = Advance();
            var name = Expect(TokenKind.Identifier);
            return new VarPatternNode(
                varToken.Span.Union(name.Span),
                TokenIdentifier(name),
                name.Span);
        }

        if (Current.Kind is TokenKind.GreaterEqual or TokenKind.LessEqual or TokenKind.Greater or TokenKind.Less)
        {
            var operation = Advance();
            // The next relational operator can start a sibling in an emitted (and/or ...) pattern.
            var operand = ParseExpression(InfixPrecedence(operation.Kind) + 1, allowSpacedCall: false);
            return new RelationalPatternNode(operation.Span.Union(operand.Span), operation.Text, operand);
        }

        if (Check(TokenKind.Identifier))
        {
            var token = Current;

            // Handle null pattern
            if (token.Text == "null")
            {
                Advance();
                return new ConstantPatternNode(token.Span, new ReferenceNode(token.Span, "null"));
            }

            if (token.Text != "_")
            {
                var saved = _position;
                var typeName = ReadInlineTypeToken(validateCanonicalSpelling: false);
                var typeSpan = GetConsumedSpan(saved);
                if (Current.Span.Line == token.Span.Line && Check(TokenKind.OpenBrace))
                {
                    ValidateCanonicalTypeSpelling(typeName, typeSpan);
                    return ParseNativePropertyPattern(typeName, typeSpan);
                }
                if (Current.Span.Line == token.Span.Line && Check(TokenKind.Identifier)
                    && Current.Text is not ("and" or "or" or "not"))
                {
                    ValidateCanonicalTypeSpelling(typeName, typeSpan);
                    var binding = Advance();
                    return new TypePatternNode(typeSpan.Union(binding.Span), typeName,
                        TokenIdentifier(binding) is "_" ? null : TokenIdentifier(binding), binding.Span, typeSpan);
                }
                if ((SyntaxFactory.ParseTypeName(typeName) is PredefinedTypeSyntax or ArrayTypeSyntax
                    or GenericNameSyntax) && !Check(TokenKind.OpenParen))
                {
                    ValidateCanonicalTypeSpelling(typeName, typeSpan);
                    return new TypePatternNode(typeSpan, typeName, null, typeNameSpan: typeSpan);
                }
                _position = saved;
            }

            Advance();
            if (token.Text == "_")
            {
                return new WildcardPatternNode(token.Span);
            }

            // Handle dotted identifier (e.g., Status.OK -- enum member constant)
            var patName = token.Text;
            if (Check(TokenKind.Dot))
            {
                while (Check(TokenKind.Dot) && Peek(1).Kind == TokenKind.Identifier)
                {
                    Advance(); // consume .
                    var next = Advance(); // consume identifier
                    patName += "." + next.Text;
                }
            }

            // Handle generic type pattern: Valid<A>, Result<T, E>
            if (Check(TokenKind.Less))
            {
                var sb3 = new System.Text.StringBuilder(patName);
                sb3.Append('<');
                Advance();
                var gd3 = 1;
                while (!IsAtEnd && gd3 > 0)
                {
                    if (Check(TokenKind.Less)) { sb3.Append('<'); gd3++; Advance(); }
                    else if (Check(TokenKind.Greater)) { sb3.Append('>'); gd3--; Advance(); }
                    else if (Check(TokenKind.GreaterGreater)) { sb3.Append(">>"); gd3 -= 2; Advance(); }
                    else if (Check(TokenKind.Comma)) { sb3.Append(", "); Advance(); }
                    else if (Check(TokenKind.Identifier) || Current.IsKeyword) { sb3.Append(Advance().Text); }
                    else if (Check(TokenKind.Question)) { sb3.Append('?'); Advance(); }
                    else if (Check(TokenKind.Dot)) { sb3.Append('.'); Advance(); }
                    else break;
                }
                patName = sb3.ToString();
            }

            // Handle positional type pattern: TypeName(subpattern) or TypeName((or ...))
            // from C# recursive patterns like BinaryExpressionSyntax(EqualsExpression)
            if (Check(TokenKind.OpenParen))
            {
                var inner = ParseParenExpressionOrInlineLambda();
                // Treat inner as an opaque sub-pattern -- the exact value doesn't matter
                // for compilation since this is a C# construct wrapped for round-tripping.
                return new ConstantPatternNode(token.Span, new CallExpressionNode(
                    token.Span, patName, new List<ExpressionNode> { inner }));
            }

            if (patName != token.Text)
            {
                return new ConstantPatternNode(token.Span, new ReferenceNode(token.Span, patName));
            }

            return new VariablePatternNode(token.Span, token.Text, token.Span);
        }

        if (Check(TokenKind.IntLiteral) || Check(TokenKind.StrLiteral) || Check(TokenKind.CharLiteral) ||
            Check(TokenKind.BoolLiteral) || Check(TokenKind.FloatLiteral) ||
            Check(TokenKind.DecimalLiteral))
        {
            var literal = ParseExpression();
            return new LiteralPatternNode(literal.Span, literal);
        }

        // Handle parenthesized patterns: (nameof x), (§PREL{gte} 5, _, _), (cast Type x), etc.
        if (Check(TokenKind.OpenParen))
        {
            // Check if content looks like a pattern (contains §VAR, §PREL, etc. anywhere inside)
            // Scan ahead through balanced parens to detect pattern tokens
            var saved = _position;
            Advance(); // consume (
            var scanDepth = 1;
            var containsPatternToken = false;
            while (!IsAtEnd && scanDepth > 0)
            {
                if (Check(TokenKind.OpenParen)) scanDepth++;
                else if (Check(TokenKind.CloseParen)) { scanDepth--; if (scanDepth == 0) break; }
                else if (Check(TokenKind.Var)
                    || Check(TokenKind.OpenBrace)  // property pattern {Prop: val}
                    || (Check(TokenKind.Identifier) && (Current.Text == "or" || Current.Text == "and" || Current.Text == "not")))
                {
                    containsPatternToken = true;
                    break;
                }
                Advance();
            }

            if (containsPatternToken)
            {
                // This is a parenthesized pattern group -- consume balanced parens
                _position = saved;
                var openParen = Advance(); // consume (
                var depth = 1;
                while (!IsAtEnd && depth > 0)
                {
                    if (Check(TokenKind.OpenParen)) depth++;
                    else if (Check(TokenKind.CloseParen)) { depth--; if (depth == 0) break; }
                    Advance();
                }
                if (Check(TokenKind.CloseParen)) Advance();
                return new WildcardPatternNode(openParen.Span);
            }
            // Otherwise, parse as expression pattern
            _position = saved;
            var expr = ParseExpression();
            // If followed by <...>, this is a generic type pattern (e.g., Valid<A>)
            // Consume the generic part and append to the reference name
            if (Check(TokenKind.Less) && expr is ReferenceNode refNode)
            {
                var sb = new System.Text.StringBuilder(refNode.Name);
                sb.Append('<');
                Advance();
                var gDepth = 1;
                while (!IsAtEnd && gDepth > 0)
                {
                    if (Check(TokenKind.Less)) { sb.Append('<'); gDepth++; Advance(); }
                    else if (Check(TokenKind.Greater)) { sb.Append('>'); gDepth--; Advance(); }
                    else if (Check(TokenKind.GreaterGreater)) { sb.Append(">>"); gDepth -= 2; Advance(); }
                    else if (Check(TokenKind.Comma)) { sb.Append(", "); Advance(); }
                    else if (Check(TokenKind.Identifier) || Current.IsKeyword) { sb.Append(Advance().Text); }
                    else if (Check(TokenKind.Question)) { sb.Append('?'); Advance(); }
                    else if (Check(TokenKind.Dot)) { sb.Append('.'); Advance(); }
                    else break;
                }
                expr = new ReferenceNode(refNode.Span, sb.ToString(), refNode.IsVerbatim);
            }
            return new ConstantPatternNode(expr.Span, expr);
        }

        // Identifier or keyword in pattern context: treat as type/constant pattern
        // Handles generic types like Valid<A>, ErrorResult<T>, dotted names like MyEnum.Value
        if (Check(TokenKind.Identifier) || Current.IsKeyword)
        {
            var startSpan = Current.Span;
            var sb2 = new System.Text.StringBuilder();
            sb2.Append(Advance().Text);

            // Handle dotted names: MyType.Value
            while (Check(TokenKind.Dot) && (Peek(1).Kind == TokenKind.Identifier || Peek(1).IsKeyword))
            {
                sb2.Append('.');
                Advance(); // consume .
                sb2.Append(Advance().Text);
            }

            // Handle generic types: Valid<A>, Result<T, E>
            if (Check(TokenKind.Less))
            {
                sb2.Append('<');
                Advance();
                var gd = 1;
                while (!IsAtEnd && gd > 0)
                {
                    if (Check(TokenKind.Less)) { sb2.Append('<'); gd++; Advance(); }
                    else if (Check(TokenKind.Greater)) { sb2.Append('>'); gd--; Advance(); }
                    else if (Check(TokenKind.GreaterGreater)) { sb2.Append(">>"); gd -= 2; Advance(); }
                    else if (Check(TokenKind.Comma)) { sb2.Append(", "); Advance(); }
                    else if (Check(TokenKind.Identifier) || Current.IsKeyword) { sb2.Append(Advance().Text); }
                    else if (Check(TokenKind.Question)) { sb2.Append('?'); Advance(); }
                    else if (Check(TokenKind.Dot)) { sb2.Append('.'); Advance(); }
                    else break;
                }
            }

            var ref2 = new ReferenceNode(startSpan, sb2.ToString());
            return new ConstantPatternNode(startSpan, ref2);
        }

        // Default: wildcard
        return new WildcardPatternNode(Current.Span);
    }

    private PatternNode ParseNativePropertyPattern(string? typeName = null, TextSpan? typeSpan = null)
    {
        var open = Expect(TokenKind.OpenBrace);
        var matches = new List<PropertyMatchNode>();
        while (!IsAtEnd && !Check(TokenKind.CloseBrace))
        {
            var name = Expect(TokenKind.Identifier);
            Expect(TokenKind.Colon);
            var start = _position;
            var pattern = ParsePattern();
            if (_position == start)
            {
                _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken,
                    "Expected a native property subpattern.");
                if (!IsAtEnd)
                    Advance();
            }
            matches.Add(new PropertyMatchNode(name.Span.Union(pattern.Span), TokenIdentifier(name), pattern));
            if (!Check(TokenKind.Comma))
                break;
            Advance();
        }
        var close = Expect(TokenKind.CloseBrace);
        var span = (typeSpan ?? open.Span).Union(close.Span);
        var property = new PropertyPatternNode(span, typeName, matches, typeSpan);
        if (Check(TokenKind.Identifier) && Current.Span.Line == close.Span.Line
            && Current.Text is not ("and" or "or" or "not"))
        {
            var binding = Advance();
            if (TokenIdentifier(binding) == "_")
                return new PropertyPatternNode(span.Union(binding.Span), typeName, matches, typeSpan);
            PatternNode capture = typeName == null
                ? new VarPatternNode(binding.Span, TokenIdentifier(binding), binding.Span)
                : new TypePatternNode(span.Union(binding.Span), typeName,
                    TokenIdentifier(binding), binding.Span, typeSpan);
            return new AndPatternNode(span.Union(binding.Span), capture,
                new PropertyPatternNode(open.Span.Union(close.Span), null, matches));
        }
        return property;
    }

    private ForStatementNode ParseForStatement()
    {
        var startToken = Expect(TokenKind.For);
        RejectLegacyLoopHeader(startToken, "for i from start to end step increment or for i = start; condition; i = next");
        var variable = Expect(TokenKind.Identifier);
        var variableType = "int";
        if (Match(TokenKind.Colon))
        {
            variableType = variable.Text;
            variable = Expect(TokenKind.Identifier);
        }
        var attrs = new AttributeCollection();
        var id = GenerateParserAutoId("l");
        ExpressionNode from;
        ExpressionNode to;
        ExpressionNode? step = null;
        ExpressionNode? condition = null;
        StatementNode? update = null;
        if (Match(TokenKind.From))
        {
            from = ParseExpression(allowSpacedCall: false);
            Expect(TokenKind.To);
            to = ParseExpression(allowSpacedCall: false);
            if (Match(TokenKind.Step))
                step = ParseExpression(allowSpacedCall: false);
        }
        else
        {
            Expect(TokenKind.Equals);
            from = ParseExpression(allowSpacedCall: false);
            Expect(TokenKind.Semicolon);
            condition = ParseExpression(allowSpacedCall: false);
            Expect(TokenKind.Semicolon);
            var target = ParseExpression(allowSpacedCall: false);
            if (Match(TokenKind.Equals))
            {
                var value = ParseExpression(allowSpacedCall: false);
                update = new AssignmentStatementNode(target.Span.Union(value.Span), target, value);
            }
            else
                update = new ExpressionStatementNode(target.Span, target);
            // The bound is descriptive only in this mode; condition/update carry
            // executable semantics and verification must not use snapshot facts.
            to = condition is BinaryOperationNode comparison ? comparison.Right : from;
        }
        var bodyOwner = BeginBlockBody(startToken, TokenKind.EndFor, id);
        var body = ParseOwnedStatementBlock(bodyOwner, TokenKind.EndFor);
        var endToken = ExpectOwnedBlockEnd(TokenKind.EndFor, bodyOwner);
        return new ForStatementNode(
            startToken.Span.Union(endToken.Span),
            id,
            TokenIdentifier(variable),
            from,
            to,
            step,
            body,
            attrs,
            variable.Span,
            condition,
            update,
            variableType);
    }

    private void RejectLegacyLoopHeader(Token token, string replacement)
    {
        if (token.Text.StartsWith('§') || Check(TokenKind.OpenBrace))
        {
            _diagnostics.ReportError(token.Span, DiagnosticCode.UnexpectedToken,
                $"Legacy loop headers and statement IDs are removed; use '{replacement}'.");
            if (Check(TokenKind.OpenBrace))
                ParseAttributes();
        }
    }

    private WhileStatementNode ParseWhileStatement()
    {
        var startToken = Expect(TokenKind.While);
        RejectLegacyLoopHeader(startToken, "while condition");
        var attrs = new AttributeCollection();
        var id = GenerateParserAutoId("wh");

        // Parse condition expression
        var condition = ParseExpression();

        // Parse body statements
        var bodyOwner = BeginBlockBody(startToken, TokenKind.EndWhile, id);
        var body = ParseOwnedStatementBlock(bodyOwner, TokenKind.EndWhile);

        var endToken = ExpectOwnedBlockEnd(TokenKind.EndWhile, bodyOwner);
        var span = startToken.Span.Union(endToken.Span);
        return new WhileStatementNode(span, id, condition, body, attrs);
    }

    private DoWhileStatementNode ParseDoWhileStatement()
    {
        var startToken = Expect(TokenKind.Do);
        RejectLegacyLoopHeader(startToken, "do followed by an indented body and a trailing while condition");
        var attrs = new AttributeCollection();
        var id = GenerateParserAutoId("do");

        // Parse body statements
        var bodyOwner = BeginBlockBody(startToken, TokenKind.EndDo, id);
        var body = ParseOwnedStatementBlock(bodyOwner, TokenKind.EndDo);

        var endToken = ExpectOwnedBlockEnd(TokenKind.EndDo, bodyOwner);
        var clause = Expect(TokenKind.While);
        if (clause.Span.Column != startToken.Span.Column || clause.Span.Line <= startToken.Span.Line)
            _diagnostics.ReportError(clause.Span, DiagnosticCode.UnexpectedToken,
                "A do loop requires a trailing 'while condition' at the do header's indentation.");
        var condition = ParseExpression(allowSpacedCall: false);

        var span = startToken.Span.Union(Current.Span);
        return new DoWhileStatementNode(span, id, body, condition, attrs);
    }

    private IfStatementNode ParseIfStatement()
    {
        var startToken = Expect(TokenKind.If);
        _lastIfToken = startToken;
        var attrs = ParseAttributes();
        ValidateIdentityFreeHeader(startToken, attrs, 0);

        // Interpret if attributes
        var id = GenerateParserAutoId("if");

        // Parse condition expression
        var condition = ParseExpression();

        // Parse then body
        var thenBody = new List<StatementNode>();
        var elseIfClauses = new List<ElseIfClauseNode>();
        List<StatementNode>? elseBody = null;

        // Check for arrow syntax: §IF{id} condition → statement
        if (Check(TokenKind.Arrow))
        {
            var lastBodySpan = startToken.Span;

            List<StatementNode> ParseClauseBody(Token clauseToken)
            {
                var body = new List<StatementNode>();
                var ownsDedent = true;
                if (Check(TokenKind.Arrow))
                {
                    var arrowToken = Advance();
                    _diagnostics.ReportError(arrowToken.Span, DiagnosticCode.UnexpectedToken,
                        "Arrow conditional statements are removed. Put the statement on an indented line under if, else if, or else.");
                    // An inline statement creates no indentation level. Its next
                    // dedent belongs to the enclosing loop, branch, or function.
                    ownsDedent = Current.IndentationDepth > arrowToken.IndentationDepth;
                    var statement = ParseStatement();
                    if (statement != null)
                        body.Add(statement);
                }
                else
                {
                    // #1485: a block clause owns only lines indented under it.
                    var clauseOwner = BeginBlockBody(clauseToken, TokenKind.EndIf, id);
                    while (!IsAtEnd && !IsBlockEnd(TokenKind.EndIf)
                        && !Check(TokenKind.Else)
                        && IsInBlockBody(clauseOwner))
                    {
                        var statement = ParseStatement();
                        if (statement != null)
                            body.Add(statement);
                    }
                    ownsDedent = IsOwnedDedent(clauseOwner);
                }

                if (body.Count > 0)
                    lastBodySpan = body[^1].Span;
                if (ownsDedent && Check(TokenKind.Dedent))
                    lastBodySpan = Advance().Span;
                return body;
            }

            thenBody = ParseClauseBody(startToken);
            var arrowChainOwner = BeginBlockBody(startToken, TokenKind.EndIf, id) with { Flat = false };

            // Parse optional §EL (else) with arrow syntax
            if (Check(TokenKind.Else) && ContinuesChain(arrowChainOwner))
            {
                var elseToken = Expect(TokenKind.Else);
                elseBody = ParseClauseBody(elseToken);
            }
            var span = startToken.Span.Union(lastBodySpan);
            return new IfStatementNode(span, id, condition, thenBody, elseIfClauses, elseBody, attrs);
        }

        // Standard multi-statement body. #1485: each clause owns only the lines
        // indented under it, so an empty clause leaves a same-column sibling
        // (and the enclosing block's Dedent) to the enclosing block.
        var clauseOwner = BeginBlockBody(startToken, TokenKind.EndIf, id);
        while (!IsAtEnd && !IsBlockEnd(TokenKind.EndIf) && !Check(TokenKind.Else)
               && IsInBlockBody(clauseOwner))
        {
            var stmt = ParseStatement();
            if (stmt != null)
            {
                thenBody.Add(stmt);
            }
        }

        // Phase 3 (indent-aware): if the body ended on its own Dedent and a
        // chain continuation (§EI/§EL) follows, consume the Dedent.
        ConsumeOwnedDedentBeforeChain(clauseOwner, TokenKind.ElseIf, TokenKind.Else);

        // Parse ELSEIF clauses
        var chainOwner = clauseOwner;
        while ((Check(TokenKind.Else) && Peek(1).Kind == TokenKind.If
                    && Peek(1).Span.Line == Current.Span.Line)
               && ContinuesChain(chainOwner))
        {
            var elseIfToken = Advance();
            if (elseIfToken.Kind == TokenKind.Else)
                Expect(TokenKind.If);
            var elseIfCondition = ParseExpression();
            var elseIfBody = new List<StatementNode>();
            if (Check(TokenKind.Arrow))
                _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken,
                    "Arrow conditional statements are removed. Put the statement on an indented line under else if.");

            clauseOwner = BeginBlockBody(elseIfToken, TokenKind.EndIf, id);
            while (!IsAtEnd && !IsBlockEnd(TokenKind.EndIf) && !Check(TokenKind.Else)
                   && IsInBlockBody(clauseOwner))
            {
                var stmt = ParseStatement();
                if (stmt != null)
                {
                    elseIfBody.Add(stmt);
                }
            }

            // Phase 3 (indent-aware): consume dangling Dedent before next §EI/§EL.
            ConsumeOwnedDedentBeforeChain(clauseOwner, TokenKind.ElseIf, TokenKind.Else);

            elseIfClauses.Add(new ElseIfClauseNode(elseIfToken.Span, elseIfCondition, elseIfBody));
        }

        // Parse ELSE clause
        if (Check(TokenKind.Else) && ContinuesChain(chainOwner))
        {
            var elseToken = Expect(TokenKind.Else);
            elseBody = new List<StatementNode>();
            if (Check(TokenKind.Arrow))
                _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken,
                    "Arrow conditional statements are removed. Put the statement on an indented line under else.");

            clauseOwner = BeginBlockBody(elseToken, TokenKind.EndIf, id);
            while (!IsAtEnd && !IsBlockEnd(TokenKind.EndIf) && IsInBlockBody(clauseOwner))
            {
                var stmt = ParseStatement();
                if (stmt != null)
                {
                    elseBody.Add(stmt);
                }
            }
            if (elseBody is [IfStatementNode nested])
            {
                var file = _diagnostics.CurrentFilePath ?? "";
                var edits = new List<TextEdit>
                {
                    TextEdit.Replace(file, elseToken.Span.Line, elseToken.Span.Column,
                        elseToken.Span.Line, elseToken.Span.Column + elseToken.Span.Length, "")
                };
                var firstIf = _tokens.First(token => token.Kind == TokenKind.If
                    && token.Span.Start >= nested.Span.Start);
                edits.Add(TextEdit.Insert(file, firstIf.Span.Line, firstIf.Span.Column, "else "));
                var width = firstIf.Span.Column - elseToken.Span.Column;
                foreach (var line in _tokens.Where(token => token.Kind is not (TokenKind.Indent or TokenKind.Dedent)
                        && token.Span.Start >= nested.Span.Start && token.Span.End <= nested.Span.End
                        && token.Span.Column > width)
                    .Select(token => token.Span.Line).Distinct())
                    edits.Add(TextEdit.Replace(file, line, 1, line, width + 1, ""));
                _diagnostics.ReportErrorWithFix(elseToken.Span, DiagnosticCode.UnexpectedToken,
                    "An else body containing only an if is removed; use an 'else if' clause.",
                    new SuggestedFix("Flatten the nested else/if into an else if clause", edits));
            }
        }

        var endToken2 = ExpectOwnedBlockEnd(TokenKind.EndIf, clauseOwner);
        var endAttrs2 = ParseAttributes();
        // Positional: [id]


        var span2 = startToken.Span.Union(endToken2.Span);
        return new IfStatementNode(span2, id, condition, thenBody, elseIfClauses, elseBody, attrs);
    }

    private BindStatementNode ParseCanonicalBinding()
    {
        var start = Advance();
        var typeStart = _position;
        var firstToken = Current;
        var first = ReadInlineTypeToken(validateCanonicalSpelling: false);
        string? type = null;
        var name = firstToken.Value as string ?? first;
        var identifierSpan = GetConsumedSpan(typeStart);
        TextSpan? typeSpan = null;
        if (Match(TokenKind.Colon))
        {
            type = first;
            ValidateCanonicalTypeSpelling(type, GetConsumedSpan(typeStart));
            typeSpan = identifierSpan;
            var identifier = Expect(TokenKind.Identifier);
            name = identifier.Value as string ?? identifier.Text;
            identifierSpan = identifier.Span;
        }
        else if (first.Length == 0 || first.IndexOfAny([':', '<', '[', '?', '.', '*']) >= 0)
        {
            _diagnostics.ReportError(identifierSpan, DiagnosticCode.TypeFirstBindingHeader,
                "A binding is 'let name = expression' or 'var type:name = expression'.");
        }
        var attributes = new AttributeCollection();
        attributes.Add("_pos0", name, identifierSpan);
        if (type != null) attributes.Add("_pos1", type, typeSpan!.Value);
        var row = TryParseSameLineRow(EffectRowPosition.Binding);
        ExpressionNode? initializer = null;
        if (Current.Span.Line == start.Span.Line && Match(TokenKind.Equals))
        {
            var continuation = Current.Span.Line > start.Span.Line
                && LineIndentColumnAt(_position) > LineIndentColumnAt(IndexOfRecentToken(start));
            if ((Current.Span.Line == start.Span.Line || continuation) && IsExpressionStart())
            {
                initializer = ParseExpression();
                if (continuation && Check(TokenKind.Dedent)
                    && Current.IndentationDepth == start.IndentationDepth)
                    Advance();
            }
            else
                _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken,
                    "A binding initializer must follow '=' on the same line or on a deeper-indented continuation.");
        }
        else if (type == null)
            _diagnostics.ReportError(start.Span, DiagnosticCode.UnexpectedToken,
                "An inferred binding requires '= expression'.");
        TryRecoverTrailingEffectRow(name, "binding");
        return new BindStatementNode(start.Span.Union(initializer?.Span ?? identifierSpan),
            name, type, start.Kind == TokenKind.Var, initializer, attributes, identifierSpan, typeSpan, row);
    }

    private AttributeCollection ParseDeclarationAttributes(Token start)
    {
        var attributes = new AttributeCollection();
        var name = Expect(TokenKind.Identifier);
        attributes.Add("_pos0", name.Value as string ?? name.Text, name.Span);
        ConsumeCanonicalDeclarationModifiers(start, attributes);
        return attributes;
    }

    private void ConsumeCanonicalDeclarationModifiers(Token start, AttributeCollection attributes)
    {
        var modifiers = new List<string>();
        var modifierSpan = attributes.GetSpan("_pos2");
        if (attributes["_pos2"] is { } existing)
            modifiers.AddRange(existing.Split(','));
        while (Current.Span.Line == start.Span.Line
            && (Check(TokenKind.Identifier) || Check(TokenKind.Unsafe)))
        {
            var token = Advance();
            var word = token.Text;
            if (Current.Span.Line == start.Span.Line
                && ((word == "protected" && Current.Text == "internal")
                    || (word == "private" && Current.Text == "protected")))
                word += "-" + Advance().Text;
            if (TryParseVisibilityWord(word, out _))
                attributes.Add("_pos1", word, token.Span);
            else if (word == "overflow" && Match(TokenKind.Equals))
                attributes.Add("overflow", Expect(TokenKind.Identifier).Text);
            else
            {
                modifiers.Add(word);
                modifierSpan = modifierSpan?.Union(token.Span) ?? token.Span;
            }
        }
        if (modifiers.Count != 0) attributes.Add("_pos2", string.Join(",", modifiers), modifierSpan ?? start.Span);
    }

    private (SinceNode? Since, DeprecatedNode? Deprecated, List<BreakingChangeNode> BreakingChanges)
        ParseDocumentationMetadata(Token start)
    {
        SinceNode? since = null;
        DeprecatedNode? deprecated = null;
        var breakingChanges = new List<BreakingChangeNode>();
        foreach (var line in (start.DocComment ?? "").Split('\n'))
        {
            var text = line.Trim();
            var fields = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 0) continue;
            if (fields[0] == "since")
            {
                if (since != null || fields.Length != 2 || !Version.TryParse(fields[1], out _))
                    _diagnostics.ReportError(start.Span, DiagnosticCode.UnexpectedToken,
                        "A declaration accepts one '/// since version' with a valid version.");
                else
                    since = new SinceNode(start.Span, fields[1], new AttributeCollection());
            }
            else if (fields[0] == "deprecated")
            {
                if (deprecated != null || fields.Length is < 2 or > 3 || !Version.TryParse(fields[1], out _))
                    _diagnostics.ReportError(start.Span, DiagnosticCode.UnexpectedToken,
                        "A declaration accepts one '/// deprecated version [replacement]'.");
                else
                    deprecated = new DeprecatedNode(start.Span, fields[1],
                        fields.Length == 3 ? fields[2] : null, null, null, new AttributeCollection());
            }
            else if (fields[0] == "breaking")
            {
                var description = fields.Length >= 3
                    ? text[(text.IndexOf(fields[1], StringComparison.Ordinal) + fields[1].Length)..].Trim()
                    : "";
                var expression = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression(description);
                if (fields.Length < 3 || !Version.TryParse(fields[1], out _)
                    || expression.ContainsDiagnostics || expression.Span.Length != description.Length
                    || expression is not Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax literal
                    || literal.RawKind != (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression)
                    _diagnostics.ReportError(start.Span, DiagnosticCode.UnexpectedToken,
                        "'/// breaking version description' requires a valid version and one C# string literal.");
                else
                    breakingChanges.Add(new BreakingChangeNode(start.Span, fields[1],
                        literal.Token.ValueText, new AttributeCollection()));
            }
        }
        return (since, deprecated, breakingChanges);
    }

    private AttributeCollection ParseTypedDeclarationAttributes(Token start, bool typeFirst)
    {
        var typeStart = _position;
        var type = ReadInlineTypeToken();
        var typeSpan = GetConsumedSpan(typeStart);
        Expect(TokenKind.Colon);
        var name = Expect(TokenKind.Identifier);
        var temporary = new AttributeCollection();
        ConsumeCanonicalDeclarationModifiers(start, temporary);
        var attributes = new AttributeCollection();
        attributes.Add(typeFirst ? "_pos0" : "_pos1", type, typeSpan);
        attributes.Add(typeFirst ? "_pos1" : "_pos0", name.Value as string ?? name.Text, name.Span);
        if (temporary["_pos1"] is { } visibility)
            attributes.Add("_pos2", visibility, temporary.GetSpan("_pos1") ?? start.Span);
        if (temporary["_pos2"] is { } modifiers)
            attributes.Add("_pos3", modifiers, temporary.GetSpan("_pos2") ?? start.Span);
        return attributes;
    }

    private AttributeCollection ParseAttributes(int maxGroups = int.MaxValue)
    {
        var attrs = new AttributeCollection();
        var groupsParsed = 0;
        var preceding = _position > 0 ? Peek(-1) : Current;
        var identityCloser = preceding.Kind is TokenKind.EndLambda or TokenKind.EndDo
            or TokenKind.EndArray or TokenKind.EndArray2D or TokenKind.EndList
            or TokenKind.EndDict or TokenKind.EndHashSet or TokenKind.EndNamespace;
        if (identityCloser && Check(TokenKind.OpenBrace))
            _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken,
                $"Source IDs are removed. Write '{preceding.Text}' without an identity header; tooling maintains .calor/ids.json.");

        // Parse structural braces {} for tag attributes
        // Note: [] is now reserved for array types to align with LLM training
        while (groupsParsed < maxGroups && Check(TokenKind.OpenBrace))
        {
            Advance(); // consume {

            // Positional format: {value1:value2:...} or {value}
            ParsePositionalAttributes(attrs);

            Expect(TokenKind.CloseBrace);
            groupsParsed++;
        }

        return attrs;
    }

    private void ValidateIdentityFreeHeader(Token token, AttributeCollection attrs, int? maximum)
    {
        var count = int.TryParse(attrs["_posCount"], out var parsed) ? parsed : 0;
        var empty = count == 1 && string.IsNullOrEmpty(attrs["_pos0"]);
        bool InvalidVisibility(int position) => attrs[$"_pos{position}"] is { Length: > 0 } value
            && !TryParseVisibilityWord(value, out _);
        static bool IntegralType(string value) => value is
            "i8" or "u8" or "i16" or "u16" or "i32" or "u32" or "i64" or "u64"
            or "byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong";
        if (maximum.HasValue && count > maximum.Value && !empty)
        {
            _diagnostics.ReportError(token.Span, DiagnosticCode.UnexpectedToken,
                $"Source IDs are removed from '{token.Text}'. Remove the leading ID field; tooling maintains declaration identity in .calor/ids.json.");
            return;
        }
        var invalidField = token.Kind switch
        {
            TokenKind.Module when !_parsingModuleMembers => attrs["_pos1"] is { } policy
                && (!policy.StartsWith("overflow=", StringComparison.Ordinal)
                    || policy["overflow=".Length..].Trim() is not ("checked" or "unchecked")),
            TokenKind.Namespace => Enumerable.Range(1, Math.Max(0, count - 1))
                .Any(index => attrs[$"_pos{index}"] is not ("global" or "named" or "file")),
            TokenKind.Func or TokenKind.AsyncFunc or TokenKind.Method or TokenKind.AsyncMethod => InvalidVisibility(1),
            TokenKind.Property => InvalidVisibility(2),
            TokenKind.Indexer => InvalidVisibility(1),
            TokenKind.Constructor => attrs["_pos0"] != "static" && InvalidVisibility(0),
            TokenKind.Event => InvalidVisibility(1),
            TokenKind.Enum => attrs["_pos2"] is { } underlying
                ? InvalidVisibility(1) || !IntegralType(underlying)
                : attrs["_pos1"] is { } visibilityOrType
                    && InvalidVisibility(1) && !IntegralType(visibilityOrType),
            _ => false
        };
        if (invalidField)
        {
            var position = token.Kind switch
            {
                TokenKind.Property => 2,
                TokenKind.Constructor => 0,
                TokenKind.Namespace => Enumerable.Range(1, count - 1)
                    .First(index => attrs[$"_pos{index}"] is not ("global" or "named" or "file")),
                TokenKind.Enum when !InvalidVisibility(1) && attrs["_pos2"] != null => 2,
                _ => 1
            };
            var expected = token.Kind switch
            {
                TokenKind.Module => "overflow=checked or overflow=unchecked",
                TokenKind.Namespace => "a namespace marker (global, named, or file)",
                TokenKind.Constructor => "a visibility or stat",
                TokenKind.Enum when position == 2 => "an integral enum underlying type",
                TokenKind.Enum when attrs["_pos2"] == null => "a visibility or integral enum underlying type",
                _ => "a visibility (public, private, protected, internal, protected internal, or private protected)"
            };
            var key = $"_pos{position}";
            var span = attrs.TryGetSpan(key, out var fieldSpan) ? fieldSpan : token.Span;
            _diagnostics.ReportError(span, DiagnosticCode.UnexpectedToken,
                $"Invalid header field '{attrs[key]}' in '{token.Text}'; expected {expected}.");
        }
    }

    private void ParsePositionalAttributes(AttributeCollection attrs)
    {
        // Parse colon-separated values: {value1:value2:value3}
        // Store them as _pos0, _pos1, _pos2, etc. for later interpretation
        var values = new List<string>();
        var position = 0;

        do
        {
            var valueStart = Current.Span;
            var startPosition = _position;
            var value = ParseValue();
            values.Add(value);
            var valueSpan = _position > startPosition
                ? valueStart.Union(Peek(-1).Span)
                : valueStart;
            attrs.Add($"_pos{position}", value, valueSpan);
            position++;
        }
        while (MatchColon());

        // Also store the raw positional count
        attrs.Add("_posCount", position.ToString());
    }

    private static TextSpan GetIdentifierSpan(
        AttributeCollection attrs,
        string key,
        string identifier,
        int leadingCharacters = 0)
    {
        if (!attrs.TryGetSpan(key, out var span))
            return TextSpan.Empty;

        var genericStart = identifier.IndexOf('<');
        var identifierLength = genericStart > 0 ? genericStart : identifier.Length;
        var length = Math.Min(identifierLength, Math.Max(0, span.Length - leadingCharacters));
        return new TextSpan(
            span.Start + leadingCharacters,
            length,
            span.Line,
            span.Column + leadingCharacters);
    }

    /// <summary>
    /// Checks if current position has an escaped brace (\{ or \}).
    /// If so, returns the brace character and advances past both tokens.
    /// </summary>
    private char? TryParseEscapedBrace()
    {
        if (Check(TokenKind.Backslash))
        {
            var next = Peek(1).Kind;
            if (next == TokenKind.OpenBrace)
            {
                Advance(); // consume backslash
                Advance(); // consume {
                return '{';
            }
            if (next == TokenKind.CloseBrace)
            {
                Advance(); // consume backslash
                Advance(); // consume }
                return '}';
            }
        }
        return null;
    }

    private string ParseValue()
    {
        var sb = new System.Text.StringBuilder();

        // Handle ~ prefix for mutability
        if (Check(TokenKind.Tilde))
        {
            sb.Append('~');
            Advance();
        }

        // Handle # prefix for semantic shortcodes
        if (Check(TokenKind.Hash))
        {
            sb.Append('#');
            Advance();
        }

        // Handle ? prefix for Option types
        if (Check(TokenKind.Question))
        {
            sb.Append('?');
            Advance();
        }

        // Handle array types like [u8], [[i32]], [str]
        if (Check(TokenKind.OpenBracket))
        {
            var depth = 0;
            while (Check(TokenKind.OpenBracket))
            {
                sb.Append('[');
                Advance();
                depth++;
            }

            // Parse the element type (may have ? prefix for nullable: [?SKPath])
            if (Check(TokenKind.Question))
            {
                sb.Append('?');
                Advance();
            }
            // Handle tuple types inside array brackets: [(T1, T2)]
            if (Check(TokenKind.OpenParen))
            {
                sb.Append('(');
                Advance();
                var parenDepth = 1;
                while (!IsAtEnd && parenDepth > 0)
                {
                    if (Check(TokenKind.OpenParen)) { sb.Append('('); parenDepth++; Advance(); }
                    else if (Check(TokenKind.CloseParen)) { parenDepth--; if (parenDepth > 0) { sb.Append(')'); Advance(); } }
                    else { sb.Append(Current.Text); Advance(); }
                }
                if (Check(TokenKind.CloseParen))
                {
                    sb.Append(')');
                    Advance();
                }
            }
            else if (Check(TokenKind.Identifier))
            {
                sb.Append(Advance().Text);

                // Handle alias-qualified names: [global::System.Type]
                if (Check(TokenKind.Colon) && Peek(1).Kind == TokenKind.Colon)
                {
                    sb.Append("::");
                    Advance(); // consume first :
                    Advance(); // consume second :
                    if (Check(TokenKind.Identifier) || Current.IsKeyword)
                    {
                        sb.Append(Advance().Text);
                    }
                }

                // Handle compound element types: [System.IO.Stream], [Riok.Models.TestObj]
                while (Current.Text == "." && Peek(1).Kind != TokenKind.CloseBracket)
                {
                    sb.Append(Advance().Text); // consume '.'
                    if (Check(TokenKind.Identifier) || Current.IsKeyword)
                    {
                        sb.Append(Advance().Text);
                    }
                }

                // Handle generic element types like [List<int>]
                if (Check(TokenKind.Less))
                {
                    sb.Append('<');
                    Advance();
                    var genericDepth = 1;
                    while (!IsAtEnd && genericDepth > 0)
                    {
                        if (Check(TokenKind.Less))
                        {
                            sb.Append('<');
                            genericDepth++;
                            Advance();
                        }
                        else if (Check(TokenKind.Greater))
                        {
                            sb.Append('>');
                            genericDepth--;
                            Advance();
                        }
                        else if (Check(TokenKind.GreaterGreater))
                        {
                            // Handle >> from nested generics like [List<Task<int>>]
                            sb.Append(">>");
                            genericDepth -= 2;
                            Advance();
                        }
                        else if (Check(TokenKind.Identifier) || Current.IsKeyword)
                        {
                            sb.Append(Advance().Text);
                        }
                        else if (Check(TokenKind.Comma))
                        {
                            sb.Append(',');
                            Advance();
                        }
                        else if (Check(TokenKind.Question))
                        {
                            sb.Append('?');
                            Advance();
                        }
                        else if (Check(TokenKind.Star))
                        {
                            sb.Append('*');
                            Advance();
                        }
                        else if (Check(TokenKind.OpenBracket))
                        {
                            sb.Append('[');
                            Advance();
                        }
                        else if (Check(TokenKind.CloseBracket))
                        {
                            sb.Append(']');
                            Advance();
                        }
                        else if (Check(TokenKind.Dot))
                        {
                            sb.Append('.');
                            Advance();
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }

            // Close the array brackets
            while (depth > 0 && Check(TokenKind.CloseBracket))
            {
                sb.Append(']');
                Advance();
                depth--;
            }

            return sb.ToString();
        }

        // Parse the main value (identifier, literal, or compound identifier)
        // Handle @identifier (C# verbatim identifier prefix) -- strip @ and use identifier
        if (Check(TokenKind.At))
        {
            Advance(); // consume @
            if (Check(TokenKind.Identifier) || Current.IsKeyword)
            {
                sb.Append(Advance().Text);
            }
        }
        else if (Check(TokenKind.Identifier))
        {
            sb.Append(Advance().Text);

            // Handle alias-qualified names: global::System.Namespace.Type
            if (Check(TokenKind.Colon) && Peek(1).Kind == TokenKind.Colon)
            {
                sb.Append("::");
                Advance(); // consume first :
                Advance(); // consume second :
                if (Check(TokenKind.Identifier) || Current.IsKeyword)
                {
                    sb.Append(Advance().Text);
                }
                // Continue to handle dot-separated parts and generics below
            }

            // Handle generic types like ILogger<MeetingModeratorService> or List<string>
            if (Check(TokenKind.Less))
            {
                sb.Append('<');
                Advance(); // consume <

                // Parse generic type arguments (may be nested)
                var depth = 1;
                var lastWasIdentifier = false;
                while (!IsAtEnd && depth > 0)
                {
                    if (Check(TokenKind.Less))
                    {
                        sb.Append('<');
                        depth++;
                        lastWasIdentifier = false;
                        Advance();
                    }
                    else if (Check(TokenKind.Greater))
                    {
                        sb.Append('>');
                        depth--;
                        lastWasIdentifier = false;
                        Advance();
                    }
                    else if (Check(TokenKind.GreaterGreater))
                    {
                        // Handle >> from nested generics like Task<List<int>>
                        sb.Append(">>");
                        depth -= 2;
                        lastWasIdentifier = false;
                        Advance();
                    }
                    else if (Check(TokenKind.Identifier))
                    {
                        // Add space between consecutive identifiers (e.g., "out T", "in T")
                        if (lastWasIdentifier) sb.Append(' ');
                        sb.Append(Advance().Text);
                        lastWasIdentifier = true;
                    }
                    else if (Check(TokenKind.Comma))
                    {
                        sb.Append(',');
                        lastWasIdentifier = false;
                        Advance();
                    }
                    else if (Check(TokenKind.Question))
                    {
                        sb.Append('?');
                        lastWasIdentifier = false;
                        Advance();
                    }
                    else if (Check(TokenKind.OpenParen))
                    {
                        // Handle tuple types in generic arguments: Dict<(Type, int), string>
                        sb.Append('(');
                        lastWasIdentifier = false;
                        Advance();
                        var parenDepth = 1;
                        while (!IsAtEnd && parenDepth > 0)
                        {
                            if (Check(TokenKind.OpenParen))
                            {
                                sb.Append('(');
                                parenDepth++;
                                Advance();
                            }
                            else if (Check(TokenKind.CloseParen))
                            {
                                parenDepth--;
                                if (parenDepth > 0)
                                    sb.Append(')');
                                Advance();
                            }
                            else
                            {
                                if (Check(TokenKind.Comma))
                                    lastWasIdentifier = false;
                                else if (Check(TokenKind.Identifier))
                                {
                                    if (lastWasIdentifier) sb.Append(' ');
                                    lastWasIdentifier = true;
                                }
                                else
                                    lastWasIdentifier = false;
                                sb.Append(Current.Text);
                                Advance();
                            }
                        }
                        sb.Append(')');
                        lastWasIdentifier = false;
                    }
                    else if (Check(TokenKind.OpenBracket))
                    {
                        // Handle array/attribute types in generic arguments with depth tracking
                        // Supports nested brackets: NULLABLE[inner=INT[bits=8][signed=false][]]
                        sb.Append('[');
                        lastWasIdentifier = false;
                        Advance();
                        var bracketDepth = 1;
                        while (!IsAtEnd && bracketDepth > 0)
                        {
                            if (Check(TokenKind.OpenBracket))
                            {
                                bracketDepth++;
                                sb.Append('[');
                                Advance();
                            }
                            else if (Check(TokenKind.CloseBracket))
                            {
                                bracketDepth--;
                                if (bracketDepth > 0)
                                {
                                    sb.Append(']');
                                    Advance();
                                }
                            }
                            else
                            {
                                sb.Append(Current.Text);
                                Advance();
                            }
                        }
                        if (Check(TokenKind.CloseBracket))
                        {
                            sb.Append(']');
                            Advance();
                        }
                    }
                    else if (Check(TokenKind.Colon) && Peek(1).Kind == TokenKind.Colon)
                    {
                        sb.Append("::");
                        lastWasIdentifier = false;
                        Advance(); // consume first :
                        Advance(); // consume second :
                    }
                    else if (Current.Text == ".")
                    {
                        sb.Append('.');
                        lastWasIdentifier = false;
                        Advance();
                    }
                    else
                    {
                        // Unknown token in generic, stop parsing
                        break;
                    }
                }
            }

            // Handle compound identifiers like Console.WriteLine
            while (Current.Text == ".")
            {
                sb.Append(Advance().Text); // consume '.'
                if (Check(TokenKind.Identifier))
                {
                    sb.Append(Advance().Text);
                }
            }

            // Handle pointer type suffix: i32* or i32**
            while (Check(TokenKind.Star) || Check(TokenKind.StarStar))
            {
                if (Check(TokenKind.StarStar))
                {
                    sb.Append("**");
                    Advance();
                }
                else
                {
                    sb.Append('*');
                    Advance();
                }
            }

            // Nullable annotations and array ranks may alternate: string?[]?[], int[,][].
            while (Check(TokenKind.OpenBracket) || Check(TokenKind.Question))
            {
                if (Match(TokenKind.Question))
                {
                    sb.Append('?');
                }
                else if (Peek(1).Kind == TokenKind.CloseBracket)
                {
                    // Simple array suffix: i32[]
                    sb.Append("[]");
                    Advance(); // consume [
                    Advance(); // consume ]
                }
                else if (Peek(1).Kind == TokenKind.Comma)
                {
                    // Multi-dim array suffix: int[,] or int[,,]
                    sb.Append('[');
                    Advance(); // consume [
                    while (Check(TokenKind.Comma))
                    {
                        sb.Append(',');
                        Advance();
                    }
                    if (Check(TokenKind.CloseBracket))
                    {
                        sb.Append(']');
                        Advance();
                    }
                }
                else
                {
                    // Bracket-delimited attribute: [key=value] or [inner=Type[nested=X]]
                    sb.Append('[');
                    Advance(); // consume [
                    var bracketDepth = 1;
                    while (!IsAtEnd && bracketDepth > 0)
                    {
                        if (Check(TokenKind.OpenBracket)) bracketDepth++;
                        else if (Check(TokenKind.CloseBracket)) { bracketDepth--; if (bracketDepth == 0) break; }
                        sb.Append(Current.Text);
                        Advance();
                    }
                    if (Check(TokenKind.CloseBracket))
                    {
                        sb.Append(']');
                        Advance();
                    }
                }
            }

            // Handle pointer type suffix: Type*, Type**
            while (Check(TokenKind.Star) || Check(TokenKind.StarStar))
            {
                if (Check(TokenKind.StarStar)) { sb.Append("**"); Advance(); continue; }
                sb.Append('*');
                Advance();
            }

            // Handle nullable type suffix: Type?, List<int>?, int[]?
            if (Check(TokenKind.Question))
            {
                sb.Append('?');
                Advance();
            }

            // Handle hyphenated identifiers like prot-int (protected internal)
            while (Check(TokenKind.Minus) && Peek(1).Kind == TokenKind.Identifier)
            {
                sb.Append('-');
                Advance(); // consume '-'
                sb.Append(Advance().Text); // consume identifier
            }

            // Handle comma-separated values like partial,static for modifiers
            // Only consume comma if followed by an identifier (not a colon or close brace)
            while (Check(TokenKind.Comma) && Peek(1).Kind == TokenKind.Identifier)
            {
                sb.Append(',');
                Advance(); // consume comma
                sb.Append(Advance().Text); // consume identifier
            }
        }
        else if (Check(TokenKind.StrLiteral))
        {
            sb.Append(Advance().Value as string ?? "");
        }
        else if (Check(TokenKind.IntLiteral) || Check(TokenKind.CharLiteral))
        {
            var token = Advance();
            sb.Append(token.Text);
        }
        else if (Check(TokenKind.BoolLiteral))
        {
            sb.Append(Advance().Value?.ToString()?.ToLowerInvariant() ?? "");
        }

        // Handle complex expressions that may contain escaped braces and other tokens
        // Continue parsing until we hit a colon (separator) or unescaped close brace (end of attributes)
        // Note: :: (double colon) for namespace aliases is NOT a separator
        while (!IsAtEnd && !(Check(TokenKind.Colon) && Peek(1).Kind != TokenKind.Colon) && !Check(TokenKind.CloseBrace))
        {
            // Handle :: namespace alias qualifier (global::System etc.)
            if (Check(TokenKind.Colon) && Peek(1).Kind == TokenKind.Colon)
            {
                sb.Append("::");
                Advance(); // consume first :
                Advance(); // consume second :
                continue;
            }
            // Handle escaped braces: \{ and \}
            var escapedBrace = TryParseEscapedBrace();
            if (escapedBrace.HasValue)
            {
                sb.Append(escapedBrace.Value);
                continue;
            }

            // Handle common expression tokens
            // Add space before identifiers if there's content before them (to separate from operators/other tokens)
            if (Check(TokenKind.Identifier))
            {
                if (sb.Length > 0 && !char.IsWhiteSpace(sb[sb.Length - 1]) &&
                    sb[sb.Length - 1] != '(' && sb[sb.Length - 1] != '!')
                    sb.Append(' ');
                sb.Append(Advance().Text);
            }
            else if (Current.Text == ".")
            {
                sb.Append('.');
                Advance();
            }
            else if (Check(TokenKind.OpenParen))
            {
                // Re-lexed header operands must not become an attached postfix call.
                if (sb.Length > 0 && sb[^1] == ')' && Peek(-1).Span.End < Current.Span.Start)
                    sb.Append(' ');
                sb.Append('(');
                Advance();
            }
            else if (Check(TokenKind.CloseParen))
            {
                sb.Append(')');
                Advance();
            }
            else if (Check(TokenKind.Comma))
            {
                // Only consume comma if not followed by close brace (end of args)
                if (Peek(1).Kind == TokenKind.CloseBrace)
                    break;
                sb.Append(',');
                Advance();
            }
            else if (Check(TokenKind.Less))
            {
                sb.Append('<');
                Advance();
            }
            else if (Check(TokenKind.Greater))
            {
                sb.Append('>');
                Advance();
            }
            else if (Check(TokenKind.GreaterGreater))
            {
                sb.Append(">>");
                Advance();
            }
            else if (Check(TokenKind.LessLess))
            {
                sb.Append("<<");
                Advance();
            }
            else if (Check(TokenKind.IntLiteral) || Check(TokenKind.CharLiteral))
            {
                // Separate literal tokens from preceding identifiers.
                if (sb.Length > 0 && !char.IsWhiteSpace(sb[sb.Length - 1]) && sb[sb.Length - 1] != '(')
                    sb.Append(' ');
                sb.Append(Advance().Text);
            }
            else if (Check(TokenKind.FloatLiteral) || Check(TokenKind.DecimalLiteral))
            {
                // Add space before floats if there's content before them
                if (sb.Length > 0 && !char.IsWhiteSpace(sb[sb.Length - 1]) && sb[sb.Length - 1] != '(')
                    sb.Append(' ');
                sb.Append(Advance().Value?.ToString() ?? "");
            }
            else if (Check(TokenKind.BoolLiteral))
            {
                // Add space before booleans if there's content before them
                if (sb.Length > 0 && !char.IsWhiteSpace(sb[sb.Length - 1]) && sb[sb.Length - 1] != '(')
                    sb.Append(' ');
                sb.Append(Advance().Value?.ToString()?.ToLowerInvariant() ?? "");
            }
            else if (Check(TokenKind.StrLiteral))
            {
                // Add space before strings if there's content before them
                if (sb.Length > 0 && !char.IsWhiteSpace(sb[sb.Length - 1]) && sb[sb.Length - 1] != '(')
                    sb.Append(' ');
                sb.Append('"');
                sb.Append(Advance().Value as string ?? "");
                sb.Append('"');
            }
            // Handle arithmetic operators for S-expressions in attributes
            // Add spaces around operators to preserve token boundaries when re-lexed
            else if (Check(TokenKind.Plus))
            {
                sb.Append(" + ");
                Advance();
            }
            else if (Check(TokenKind.Minus))
            {
                sb.Append(" - ");
                Advance();
            }
            else if (Check(TokenKind.Star))
            {
                sb.Append(" * ");
                Advance();
            }
            else if (Check(TokenKind.Slash))
            {
                sb.Append(" / ");
                Advance();
            }
            else if (Check(TokenKind.Percent))
            {
                sb.Append(" % ");
                Advance();
            }
            // Comparison operators
            else if (Check(TokenKind.EqualEqual))
            {
                sb.Append(" == ");
                Advance();
            }
            else if (Check(TokenKind.BangEqual))
            {
                sb.Append(" != ");
                Advance();
            }
            else if (Check(TokenKind.LessEqual))
            {
                sb.Append(" <= ");
                Advance();
            }
            else if (Check(TokenKind.GreaterEqual))
            {
                sb.Append(" >= ");
                Advance();
            }
            // Note: Less and Greater are already handled above for generic types
            // but we need them for comparisons too - they get special handling
            // Logical operators
            else if (Check(TokenKind.AmpAmp))
            {
                sb.Append(" && ");
                Advance();
            }
            else if (Check(TokenKind.PipePipe))
            {
                sb.Append(" || ");
                Advance();
            }
            else if (Check(TokenKind.Exclamation))
            {
                sb.Append("!");
                Advance();
            }
            // Bitwise operators
            else if (Check(TokenKind.Amp))
            {
                sb.Append(" & ");
                Advance();
            }
            else if (Check(TokenKind.Pipe))
            {
                sb.Append(" | ");
                Advance();
            }
            else if (Check(TokenKind.Caret))
            {
                sb.Append(" ^ ");
                Advance();
            }
            else if (Check(TokenKind.OpenBracket))
            {
                // Handle array suffix/indexer with depth tracking: [0], [..expr[0]]
                sb.Append('[');
                Advance();
                int bracketDepth2 = 1;
                while (!IsAtEnd && bracketDepth2 > 0)
                {
                    if (Check(TokenKind.OpenBracket)) { bracketDepth2++; sb.Append('['); Advance(); }
                    else if (Check(TokenKind.CloseBracket)) { bracketDepth2--; if (bracketDepth2 > 0) { sb.Append(']'); Advance(); } }
                    else { sb.Append(Current.Text); Advance(); }
                }
                if (Check(TokenKind.CloseBracket))
                {
                    sb.Append(']');
                    Advance();
                }
            }
            else if (Check(TokenKind.OpenBrace))
            {
                // Handle nested braces from raw C# in converter output: { ... }
                sb.Append('{');
                Advance();
                var braceDepth = 1;
                while (!IsAtEnd && braceDepth > 0)
                {
                    if (Check(TokenKind.OpenBrace)) braceDepth++;
                    else if (Check(TokenKind.CloseBrace)) { braceDepth--; if (braceDepth == 0) break; }
                    sb.Append(Current.Text);
                    Advance();
                }
                if (Check(TokenKind.CloseBrace))
                {
                    sb.Append('}');
                    Advance();
                }
            }
            else if (Check(TokenKind.Question))
            {
                // Handle nullable suffix: Type?
                sb.Append('?');
                Advance();
            }
            else if (Check(TokenKind.Hash))
            {
                // Handle # in identifiers (e.g., source generator snapshot filenames)
                sb.Append('#');
                Advance();
            }
            else if (Check(TokenKind.NullCoalesce))
            {
                sb.Append(" ?? ");
                Advance();
            }
            else if (Check(TokenKind.NullConditional))
            {
                sb.Append("?.");
                Advance();
            }
            else if (Check(TokenKind.Equals))
            {
                sb.Append('=');
                Advance();
            }
            else if (Check(TokenKind.Slash))
            {
                sb.Append('/');
                Advance();
            }
            else if (Check(TokenKind.At))
            {
                sb.Append('@');
                Advance();
            }
            else if (Check(TokenKind.Backslash))
            {
                sb.Append('\\');
                Advance();
            }
            else
            {
                // Unknown token, stop parsing this value
                break;
            }
        }

        return sb.ToString();
    }

    private bool MatchColon()
    {
        // Don't match single ':' if followed by another ':' -- that's '::' (alias qualifier)
        if (Check(TokenKind.Colon) && Peek(1).Kind != TokenKind.Colon)
        {
            Advance();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Parses C#-style attributes in the format [@AttributeName] or [@AttributeName(args)].
    /// These appear after the structural brackets: §CLASS[id:name][@Route("api")][@ApiController]
    /// </summary>
    private IReadOnlyList<CalorAttributeNode> ParseCSharpAttributes()
    {
        var attributes = new List<CalorAttributeNode>();

        // Keep parsing while we see [@
        while (Check(TokenKind.OpenBracket)
            && (Peek(1).Kind == TokenKind.At
                || Peek(1).Kind == TokenKind.Identifier && Peek(1).Text.StartsWith('@')))
        {
            var startToken = Advance(); // consume [
            var attachedMarker = !Check(TokenKind.At);
            if (Check(TokenKind.At)) Advance();

            // Parse optional target prefix: [@target:Name(args)]
            // Accept any identifier before ':' as a target, matching C# grammar.
            // The identifier after ':' must also be present (the attribute name).
            // Exclude '::' (alias-qualified names like global::System...) -- only match single ':'.
            string? target = null;
            if (Check(TokenKind.Identifier) && Peek(1).Kind == TokenKind.Colon
                && Peek(2).Kind != TokenKind.Colon
                && Peek(2).Kind == TokenKind.Identifier)
            {
                target = Current.Value as string ?? Current.Text;
                Advance(); // consume target
                Advance(); // consume :
                attachedMarker = false;
            }

            // Parse attribute name
            if (!Check(TokenKind.Identifier))
            {
                _diagnostics.ReportUnexpectedToken(Current.Span, "attribute name", Current.Kind);
                // Try to recover by finding the closing bracket
                while (!IsAtEnd && !Check(TokenKind.CloseBracket))
                {
                    Advance();
                }
                if (Check(TokenKind.CloseBracket))
                {
                    Advance();
                }
                continue;
            }

            var nameToken = Advance();
            var name = attachedMarker ? nameToken.Value as string ?? nameToken.Text[1..] : nameToken.Text;

            // Handle alias-qualified names like global::System.ComponentModel.Description
            if (Check(TokenKind.Colon) && Peek(1).Kind == TokenKind.Colon)
            {
                Advance(); // consume first :
                Advance(); // consume second :
                name += "::";
                if (Check(TokenKind.Identifier))
                {
                    name += Advance().Text;
                }
            }

            // Handle qualified names like System.ComponentModel.Description
            while (Current.Text == ".")
            {
                Advance(); // consume .
                if (Check(TokenKind.Identifier))
                {
                    name += "." + Advance().Text;
                }
            }

            // Handle generic type arguments: [@JSMarshalAs<JSType.Number>]
            if (Check(TokenKind.Less))
            {
                name += "<";
                Advance(); // consume <
                var genDepth = 1;
                while (!IsAtEnd && genDepth > 0)
                {
                    if (Check(TokenKind.Less)) { genDepth++; name += "<"; Advance(); }
                    else if (Check(TokenKind.Greater)) { genDepth--; name += ">"; Advance(); }
                    else if (Check(TokenKind.GreaterGreater)) { genDepth -= 2; name += ">>"; Advance(); }
                    else { name += Current.Text; Advance(); }
                }
            }

            var arguments = new List<CalorAttributeArgument>();

            // Check for arguments: [@Attr(args)]
            if (Check(TokenKind.OpenParen))
            {
                Advance(); // consume (
                arguments = ParseCSharpAttributeArguments();
                Expect(TokenKind.CloseParen);
            }

            Expect(TokenKind.CloseBracket);

            var span = startToken.Span.Union(Current.Span);
            attributes.Add(new CalorAttributeNode(span, name, arguments, target));
        }

        return attributes;
    }

    /// <summary>
    /// Parses the arguments inside a C# attribute: (arg1, "arg2", Name=value)
    /// </summary>
    private List<CalorAttributeArgument> ParseCSharpAttributeArguments()
    {
        var args = new List<CalorAttributeArgument>();

        if (Check(TokenKind.CloseParen))
        {
            return args;
        }

        do
        {
            // Check for named argument: Name=value
            if (Check(TokenKind.Identifier) && Peek(1).Kind == TokenKind.Equals)
            {
                var nameToken = Advance();
                Advance(); // consume =
                var value = ParseCSharpAttributeValue();
                args.Add(new CalorAttributeArgument(nameToken.Text, value));
            }
            else
            {
                // Positional argument
                var value = ParseCSharpAttributeValue();
                args.Add(new CalorAttributeArgument(value));
            }
        }
        while (MatchComma());

        return args;
    }

    /// <summary>
    /// Parses a value in a C# attribute argument, including bitwise expressions with proper precedence.
    /// Grammar: bitwiseOr → bitwiseXor ('|' bitwiseXor)*
    /// </summary>
    private object ParseCSharpAttributeValue()
    {
        var left = ParseCSharpAttributeXor();
        while (Check(TokenKind.Pipe))
        {
            Advance(); // consume |
            var right = ParseCSharpAttributeXor();
            left = new BitwiseBinaryExpression(left, BitwiseOperator.Or, right);
        }
        return left;
    }

    /// <summary>
    /// bitwiseXor → bitwiseAnd ('^' bitwiseAnd)*
    /// </summary>
    private object ParseCSharpAttributeXor()
    {
        var left = ParseCSharpAttributeAnd();
        while (Check(TokenKind.Caret))
        {
            Advance(); // consume ^
            var right = ParseCSharpAttributeAnd();
            left = new BitwiseBinaryExpression(left, BitwiseOperator.Xor, right);
        }
        return left;
    }

    /// <summary>
    /// bitwiseAnd → unary ('&amp;' unary)*
    /// </summary>
    private object ParseCSharpAttributeAnd()
    {
        var left = ParseCSharpAttributeUnary();
        while (Check(TokenKind.Amp))
        {
            Advance(); // consume &
            var right = ParseCSharpAttributeUnary();
            left = new BitwiseBinaryExpression(left, BitwiseOperator.And, right);
        }
        return left;
    }

    /// <summary>
    /// unary → '~' unary | atom
    /// </summary>
    private object ParseCSharpAttributeUnary()
    {
        if (Check(TokenKind.Tilde))
        {
            Advance(); // consume ~
            var operand = ParseCSharpAttributeUnary();
            return new BitwiseNotExpression(operand);
        }
        return ParseCSharpAttributeAtom();
    }

    /// <summary>
    /// atom → '(' bitwiseOr ')' | primary
    /// </summary>
    private object ParseCSharpAttributeAtom()
    {
        if (Check(TokenKind.OpenParen))
        {
            Advance(); // consume (
            var inner = ParseCSharpAttributeValue();
            Expect(TokenKind.CloseParen);
            return inner;
        }
        return ParseCSharpAttributePrimaryValue();
    }

    /// <summary>
    /// Parses a single primary value in a C# attribute argument (literal, identifier, typeof, qualified name).
    /// </summary>
    private object ParseCSharpAttributePrimaryValue()
    {
        if (Check(TokenKind.CharLiteral))
        {
            return (char)Advance().Value!;
        }
        if (Check(TokenKind.StrLiteral))
        {
            var token = Advance();
            if (token.Value is string value && !token.Text.EndsWith("u8", StringComparison.Ordinal))
                return value;
            _diagnostics.ReportError(token.Span, DiagnosticCode.UnexpectedToken,
                "C# attribute string arguments must be constant ordinary strings; escape '${' as '\\${' for literal text.");
            return "";
        }
        if (Check(TokenKind.IntLiteral))
        {
            var value = Advance().Value;
            return value is IntLiteralInfo info
                ? info.IsUnsigned
                    ? info.Width == IntegerLiteralWidth.Bits32
                        ? (object)(uint)info.UnsignedValue
                        : info.UnsignedValue
                    : info.Width == IntegerLiteralWidth.Bits32
                        ? (object)checked((int)info.SignedValue)
                        : info.SignedValue
                : value ?? 0;
        }
        if (Check(TokenKind.FloatLiteral) || Check(TokenKind.DecimalLiteral))
        {
            return Advance().Value ?? 0.0;
        }
        if (Check(TokenKind.BoolLiteral))
        {
            return Advance().Value ?? false;
        }
        if (Check(TokenKind.Identifier))
        {
            var text = Advance().Text;

            // Handle alias-qualified names: global::System.Type
            if (Check(TokenKind.Colon) && Peek(1).Kind == TokenKind.Colon)
            {
                text += "::";
                Advance(); // consume first :
                Advance(); // consume second :
                if (Check(TokenKind.Identifier) || Current.IsKeyword)
                {
                    text += Advance().Text;
                }
            }

            // Handle typeof(TypeName)
            if (text == "typeof" && Check(TokenKind.OpenParen))
            {
                Advance(); // consume (
                var typeName = "";
                while (!IsAtEnd && !Check(TokenKind.CloseParen))
                {
                    typeName += Advance().Text;
                }
                Expect(TokenKind.CloseParen);
                return new TypeOfReference(typeName);
            }

            // Handle nameof(identifier)
            if (text == "nameof" && Check(TokenKind.OpenParen))
            {
                Advance(); // consume (
                var name = "";
                while (!IsAtEnd && !Check(TokenKind.CloseParen))
                {
                    name += Advance().Text;
                }
                Expect(TokenKind.CloseParen);
                return new NameOfReference(name);
            }

            // Handle qualified names like System.String or enum values like AccessLevel.Admin
            while (Current.Text == ".")
            {
                text += Advance().Text; // consume .
                if (Check(TokenKind.Identifier) || Current.IsKeyword)
                {
                    text += Advance().Text;
                }
            }

            // Handle generic type names: CascadingValueSource<MyParamType>
            if (Check(TokenKind.Less))
            {
                text += "<";
                Advance();
                var gDepth = 1;
                while (!IsAtEnd && gDepth > 0)
                {
                    if (Check(TokenKind.Less)) { text += "<"; gDepth++; Advance(); }
                    else if (Check(TokenKind.Greater)) { text += ">"; gDepth--; Advance(); }
                    else if (Check(TokenKind.GreaterGreater)) { text += ">>"; gDepth -= 2; Advance(); }
                    else { text += Advance().Text; }
                }
                // Continue with dotted access after generic: Type<T>.Member
                while (Current.Text == ".")
                {
                    text += Advance().Text;
                    if (Check(TokenKind.Identifier) || Current.IsKeyword)
                        text += Advance().Text;
                }
            }

            return new MemberAccessReference(text);
        }

        // Unrecognized value - return empty string
        _diagnostics.ReportUnexpectedToken(Current.Span, "attribute value", Current.Kind);
        return "";
    }

    private bool MatchComma()
    {
        if (Check(TokenKind.Comma))
        {
            Advance();
            return true;
        }
        return false;
    }

    /// <summary>
    /// The visibility words the parser accepts, in the order the
    /// <see cref="DiagnosticCode.UnknownVisibility"/> message lists them.
    /// </summary>
    internal const string ValidVisibilityWords =
        "public, internal, protected, private, protected internal, private protected";

    /// <summary>
    /// Maps a visibility word to its <see cref="Visibility"/>. Returns false for a
    /// word that is not a visibility (#1607: such a word used to compile silently
    /// as <c>private</c>).
    /// </summary>
    private static bool TryParseVisibilityWord(string? value, out Visibility visibility)
    {
        Visibility? parsed = value switch
        {
            "public" => Visibility.Public,
            "protected-internal" => Visibility.ProtectedInternal,
            "private-protected" => Visibility.PrivateProtected,
            "internal" => Visibility.Internal,
            "protected" => Visibility.Protected,
            "private" => Visibility.Private,
            _ => null
        };
        visibility = parsed ?? Visibility.Public;
        return parsed.HasValue;
    }

    /// <summary>
    /// Parses a visibility word. An absent or empty word means the declaration's
    /// default (<c>private</c>); any other unknown word is a hard error (#1607).
    /// </summary>
    private Visibility ParseVisibility(string? value, TextSpan span)
    {
        if (TryParseVisibilityWord(value, out var visibility) || string.IsNullOrEmpty(value))
            return visibility;
        // A modifier in the visibility slot (`§MT{m1:Area:abs}`) used to drop the modifier.
        var replacement = RemovedVisibilityReplacement(value);
        var hint = replacement != null
            ? $" Use '{replacement}' instead."
            : ParseMethodModifiers(value) != MethodModifiers.None
            ? $" '{value}' is a modifier: it follows the visibility, e.g. 'public {value}'."
            : "";
        _diagnostics.ReportError(span, DiagnosticCode.UnknownVisibility,
            $"Unknown visibility '{value}'. Valid visibility words: {ValidVisibilityWords}.{hint}");
        return visibility;
    }

    // 0.30 (#1596): visibility is spelled as in C#; the old short words are errors
    // that name their replacement.
    private static string? RemovedVisibilityReplacement(string? value) => value switch
    {
        "pub" => "public",
        "int" => "internal",
        "prot" or "pro" => "protected",
        "priv" or "pri" => "private",
        "prot-int" => "protected internal",
        "priv-prot" => "private protected",
        _ => null
    };

    /// <summary>
    /// Parses foreach statement.
    /// §EACH[each1:item:i32] §REF[name=arr]      // foreach (int item in arr)
    ///   ...
    /// §/EACH[each1]
    /// </summary>
    private StatementNode ParseForeachStatement()
    {
        var startToken = Expect(TokenKind.Foreach);
        RejectLegacyLoopHeader(startToken, "each item in collection or each key, value in collection");
        var attrs = new AttributeCollection();
        var id = GenerateParserAutoId("each");
        var variableType = "var";
        Token variable;
        if (IsTypedBinderAhead(TokenKind.In, TokenKind.Comma))
        {
            variableType = ReadInlineTypeToken();
            Expect(TokenKind.Colon);
            variable = Expect(TokenKind.Identifier);
        }
        else
        {
            variable = Expect(TokenKind.Identifier);
        }
        Token? secondVariable = null;
        if (Match(TokenKind.Comma))
            secondVariable = Expect(TokenKind.Identifier);
        Expect(TokenKind.In);
        var collection = ParseExpression(allowSpacedCall: false);

        // Parse body statements
        var bodyOwner = BeginBlockBody(startToken, TokenKind.EndForeach, id);
        var body = ParseOwnedStatementBlock(bodyOwner, TokenKind.EndForeach);

        var endToken = ExpectOwnedBlockEnd(TokenKind.EndForeach, bodyOwner);
        var span = startToken.Span.Union(endToken.Span);
        if (secondVariable is { } value)
            return new DictionaryForeachNode(span, id, TokenIdentifier(variable), TokenIdentifier(value), collection, body, attrs,
                variable.Span, value.Span);
        return new ForeachStatementNode(
            span,
            id,
            TokenIdentifier(variable),
            variableType,
            collection,
            body,
            attrs,
            variableSpan: variable.Span);
    }

    // Phase 7: Generics

    /// <summary>
    /// Parses an optional type parameter list in angle bracket syntax.
    /// &lt;T&gt; or &lt;T, U&gt; or &lt;TKey, TValue&gt;
    /// Returns an empty list if no type parameters are present.
    /// </summary>
    private List<TypeParameterNode> ParseOptionalTypeParameterList(
        TextSpan defaultSpan,
        bool allowVariance = false,
        string owner = "this declaration",
        List<EffectParameterInfo>? effectParameters = null,
        string? effectOwner = null)
    {
        var typeParams = new List<TypeParameterNode>();

        if (!Check(TokenKind.Less))
        {
            return typeParams;
        }

        var startToken = Advance(); // consume <

        do
        {
            if (Match(TokenKind.Exclamation))
            {
                var effNameToken = Expect(TokenKind.Identifier);
                RecordEffectBinder(effNameToken, effectParameters, typeParams.Count, effectOwner ?? owner);
                continue;
            }

            // Check for variance modifiers: in/out before the type parameter name
            var variance = Ast.VarianceKind.None;
            if (Current.Text is "in" or "out")
            {
                variance = Current.Text == "out" ? Ast.VarianceKind.Out : Ast.VarianceKind.In;
                if (!allowVariance)
                {
                    _diagnostics.ReportError(
                        Current.Span,
                        DiagnosticCode.InvalidTypeParameterVariance,
                        $"Type parameter variance is only legal on interfaces and delegates, not {owner}.");
                }
                Advance(); // consume in/out
            }

            if (Check(TokenKind.Identifier))
            {
                var nameToken = Advance();
                typeParams.Add(new TypeParameterNode(nameToken.Span, nameToken.Value as string ?? nameToken.Text, Array.Empty<TypeConstraintNode>(), variance));
            }
            else
            {
                _diagnostics.ReportUnexpectedToken(Current.Span, "type parameter name", Current.Kind);
                break;
            }
        } while (Match(TokenKind.Comma));

        Expect(TokenKind.Greater); // >
        return typeParams;
    }

    /// <summary>
    /// Records — or rejects — one <c>eff</c> binder from a type-parameter list.
    /// Two rejections, both Calor0404 and both at parse time:
    /// the declaration form may not bind effect variables at all (§7.3's
    /// class/interface-level row), or the binder's name collides with the effect
    /// taxonomy, which would make that code unwritable inside the declaration
    /// (§7.2(c)). An ordinary type parameter named after a code — <b>Z6</b>'s
    /// <c>&lt;T, cw&gt;</c> — is untouched by both: the ban is on <c>eff</c> names.
    /// </summary>
    private void RecordEffectBinder(
        Token nameToken,
        List<EffectParameterInfo>? effectParameters,
        int typeParameterCount,
        string owner)
    {
        var name = nameToken.Value as string ?? nameToken.Text;

        if (effectParameters is null)
        {
            _diagnostics.ReportError(
                nameToken.Span,
                DiagnosticCode.EffectVariableScope,
                $"An 'eff' effect variable cannot be declared on {owner}. In 0.15 an effect "
                + "variable may only be bound by a function or method declaration — write it on "
                + "the member's own type-parameter list, for example func Handle<eff e> public (...).");
            return;
        }

        if (EffectCodes.KnownCompactCodes.Contains(name, StringComparer.OrdinalIgnoreCase)
            || EffectCodes.ColonPrefixes.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            _diagnostics.ReportError(
                nameToken.Span,
                DiagnosticCode.EffectVariableScope,
                $"Effect variable 'eff {name}' is named after the effect code '{name}'. "
                + "Effect variables are resolved before the effect taxonomy, so this name would "
                + "make the code unwritable inside this declaration. Rename the variable.");
            return;
        }

        effectParameters.Add(new EffectParameterInfo(
            name,
            typeParameterCount + effectParameters.Count,
            nameToken.Span));
    }

    /// <summary>
    /// Pushes an <c>eff</c> binder scope for the declaration being parsed. Dispose
    /// pops it, so nesting is correct even when a declaration's body throws.
    /// </summary>
    private EffectVariableScope PushEffectVariableScope(IReadOnlyList<EffectParameterInfo> binders)
    {
        _effectVariableScopes.Add(binders);
        return new EffectVariableScope(this);
    }

    private readonly struct EffectVariableScope : IDisposable
    {
        private readonly Parser _parser;

        internal EffectVariableScope(Parser parser) => _parser = parser;

        public void Dispose() => _parser._effectVariableScopes.RemoveAt(
            _parser._effectVariableScopes.Count - 1);
    }

    /// <summary>
    /// Parses a WHERE clause and adds constraints to the appropriate type parameter.
    /// New syntax: §WHERE T : class, IComparable&lt;T&gt;
    /// Braced syntax: §WHERE{T:constraint1,constraint2}
    /// </summary>
    private void ParseWhereClause(List<TypeParameterNode> typeParameters)
    {
        var startToken = Expect(TokenKind.Where);

        string typeParamName;
        var constraints = new List<string>();

        // Check for new syntax: §WHERE T : constraint1, constraint2
        // New syntax has identifier directly after §WHERE, not in braces
        if (Check(TokenKind.Identifier))
        {
            // NEW SYNTAX: §WHERE T : class, IComparable<T>
            var nameToken = Advance();
            typeParamName = nameToken.Text;

            Expect(TokenKind.Colon); // :

            // Parse constraints using specialized method that handles <> depth
            constraints = ParseWhereConstraintList();
        }
        else if (Check(TokenKind.OpenBrace))
        {
            // Braced syntax: §WHERE{T:constraint1,constraint2}
            var attrs = ParseAttributes();

            typeParamName = attrs["_pos0"] ?? "";
            var constraintStr = attrs["_pos1"] ?? "";

            if (string.IsNullOrEmpty(typeParamName))
            {
                _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "WHERE", "type parameter name");
                return;
            }

            // Parse comma-separated constraints from attribute string
            constraints = constraintStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim())
                .Where(c => !string.IsNullOrEmpty(c))
                .ToList();
        }
        else
        {
            _diagnostics.ReportUnexpectedToken(Current.Span, "type parameter name or {", Current.Kind);
            return;
        }

        // Find the type parameter to add the constraint to
        var typeParamIndex = typeParameters.FindIndex(tp => tp.Name == typeParamName);
        if (typeParamIndex < 0)
        {
            _diagnostics.ReportError(startToken.Span, DiagnosticCode.TypeParameterNotFound,
                $"Type parameter '{typeParamName}' not found");
            return;
        }

        // Convert constraint strings to TypeConstraintNodes
        var newConstraints = typeParameters[typeParamIndex].Constraints.ToList();

        foreach (var constraintText in constraints)
        {
            var (kind, typeName) = constraintText switch
            {
                "class" => (TypeConstraintKind.Class, (string?)null),
                "class?" => (TypeConstraintKind.ClassNullable, (string?)null),
                "struct" => (TypeConstraintKind.Struct, (string?)null),
                "new" or "new()" => (TypeConstraintKind.New, (string?)null),
                "notnull" => (TypeConstraintKind.NotNull, (string?)null),
                "unmanaged" => (TypeConstraintKind.Unmanaged, (string?)null),
                "default" => (TypeConstraintKind.Default, (string?)null),
                "allows ref struct" => (TypeConstraintKind.AllowsRefStruct, (string?)null),
                _ => (TypeConstraintKind.TypeName, constraintText)
            };

            var constraint = new TypeConstraintNode(startToken.Span, kind, typeName);
            newConstraints.Add(constraint);
        }

        // Replace the type parameter with one that includes the new constraints
        var oldTypeParam = typeParameters[typeParamIndex];
        typeParameters[typeParamIndex] = new TypeParameterNode(
            oldTypeParam.Span,
            oldTypeParam.Name,
            newConstraints,
            oldTypeParam.Variance);
    }

    /// <summary>
    /// Parses a comma-separated list of constraints in WHERE clause.
    /// Handles generic types like IComparable&lt;T&gt; correctly.
    /// </summary>
    private List<string> ParseWhereConstraintList()
    {
        var constraints = new List<string>();

        do
        {
            var constraint = ParseConstraintTypeName();
            if (!string.IsNullOrEmpty(constraint))
            {
                constraints.Add(constraint);
            }
        } while (Match(TokenKind.Comma));

        return constraints;
    }

    /// <summary>
    /// Parses a single constraint type name, including generic types.
    /// Handles: class, struct, new(), IComparable, IComparable&lt;T&gt;, List&lt;int&gt;
    /// </summary>
    private string ParseConstraintTypeName()
    {
        // Handle special keywords
        if (Check(TokenKind.Identifier) || Current.IsKeyword)
        {
            var text = Current.Text;
            if (text == "class")
            {
                Advance();
                if (Match(TokenKind.Question))
                {
                    return "class?";
                }
                return text;
            }
            if (text == "struct" || text == "notnull" || text == "unmanaged" || text == "default")
            {
                Advance();
                return text;
            }
            if (text == "allows"
                && Peek(1).Kind == TokenKind.Identifier
                && Peek(1).Text.Equals("ref", StringComparison.Ordinal)
                && Peek(2).Kind == TokenKind.Identifier
                && Peek(2).Text.Equals("struct", StringComparison.Ordinal))
            {
                Advance();
                Advance();
                Advance();
                return "allows ref struct";
            }
            if (text == "new")
            {
                Advance();
                // Check for optional ()
                if (Check(TokenKind.OpenParen))
                {
                    Advance();
                    Expect(TokenKind.CloseParen);
                }
                return "new()";
            }
        }

        // Parse type name with potential generic arguments
        var sb = new System.Text.StringBuilder();

        if (!Check(TokenKind.Identifier))
        {
            _diagnostics.ReportUnexpectedToken(Current.Span, "constraint type name", Current.Kind);
            return "";
        }

        sb.Append(Advance().Text);

        // Handle alias-qualified names: global::System.Type
        if (Check(TokenKind.Colon) && Peek(1).Kind == TokenKind.Colon)
        {
            sb.Append("::");
            Advance(); // consume first :
            Advance(); // consume second :
            if (Check(TokenKind.Identifier) || Current.IsKeyword)
            {
                sb.Append(Advance().Text);
            }
        }

        // Handle dot-qualified names: Namespace.Type
        while (Check(TokenKind.Dot) && (Peek(1).Kind == TokenKind.Identifier || Peek(1).IsKeyword))
        {
            sb.Append('.');
            Advance(); // consume .
            sb.Append(Advance().Text);
        }

        // Handle generic type arguments: <T, U>
        if (Check(TokenKind.Less))
        {
            sb.Append('<');
            Advance();

            var depth = 1;
            while (!IsAtEnd && depth > 0)
            {
                if (Check(TokenKind.Less))
                {
                    sb.Append('<');
                    depth++;
                    Advance();
                }
                else if (Check(TokenKind.Greater))
                {
                    sb.Append('>');
                    depth--;
                    Advance();
                }
                else if (Check(TokenKind.GreaterGreater))
                {
                    // >> closes two generic levels at once (e.g., List<Task<int>>)
                    sb.Append(">>");
                    depth -= 2;
                    Advance();
                }
                else if (Check(TokenKind.Comma))
                {
                    // Comma inside <> is a type argument separator
                    sb.Append(", ");
                    Advance();
                }
                else if (Check(TokenKind.Colon) && Peek(1).Kind == TokenKind.Colon)
                {
                    sb.Append("::");
                    Advance(); // consume first :
                    Advance(); // consume second :
                }
                else if (Check(TokenKind.Dot))
                {
                    sb.Append('.');
                    Advance();
                }
                else if (Check(TokenKind.Identifier) || Current.IsKeyword)
                {
                    sb.Append(Current.Text);
                    Advance();
                }
                else if (Check(TokenKind.Question))
                {
                    sb.Append('?');
                    Advance();
                }
                else if (Check(TokenKind.Star))
                {
                    sb.Append('*');
                    Advance();
                }
                else if (Check(TokenKind.OpenBracket))
                {
                    sb.Append('[');
                    Advance();
                }
                else if (Check(TokenKind.CloseBracket))
                {
                    sb.Append(']');
                    Advance();
                }
                else
                {
                    // Unknown token - stop parsing
                    break;
                }
            }
        }

        // Handle dot-qualified nested types after generics: Type<T>.NestedType
        while (Check(TokenKind.Dot) && (Peek(1).Kind == TokenKind.Identifier || Peek(1).IsKeyword))
        {
            sb.Append('.');
            Advance(); // consume .
            sb.Append(Advance().Text);
            // Handle another generic level on the nested type: Outer<T>.Inner<U>
            if (Check(TokenKind.Less))
            {
                sb.Append('<');
                Advance();
                var innerDepth = 1;
                while (!IsAtEnd && innerDepth > 0)
                {
                    if (Check(TokenKind.Less)) { sb.Append('<'); innerDepth++; Advance(); }
                    else if (Check(TokenKind.Greater)) { sb.Append('>'); innerDepth--; Advance(); }
                    else if (Check(TokenKind.GreaterGreater)) { sb.Append(">>"); innerDepth -= 2; Advance(); }
                    else if (Check(TokenKind.Comma)) { sb.Append(", "); Advance(); }
                    else if (Check(TokenKind.Dot)) { sb.Append('.'); Advance(); }
                    else if (Check(TokenKind.Identifier)) { sb.Append(Current.Text); Advance(); }
                    else if (Check(TokenKind.Question)) { sb.Append('?'); Advance(); }
                    else break;
                }
            }
        }

        // Handle nullable suffix: IEffect?, Nullable<T>?
        if (Check(TokenKind.Question))
        {
            sb.Append('?');
            Advance();
        }

        // Handle array suffix: T[]
        if (Check(TokenKind.OpenBracket) && Peek(1).Kind == TokenKind.CloseBracket)
        {
            sb.Append("[]");
            Advance();
            Advance();
        }

        return sb.ToString();
    }

    // Phase 8: Classes, Interfaces, Inheritance

    /// <summary>
    /// Parses an interface definition.
    /// §IFACE[i001:IShape]
    ///   §METHOD[m001:Area] §O[f64] §E[] §/METHOD[m001]
    /// §/IFACE[i001]
    /// </summary>
    private InterfaceDefinitionNode ParseInterfaceDefinition()
    {
        var startToken = Expect(TokenKind.Interface);
        var attrs = ParseDeclarationAttributes(startToken);
        ValidateIdentityFreeHeader(startToken, attrs, 2);
        var csharpAttrs = ParseCSharpAttributes();

        // Positional: [id:name:baseInterface?]
        var id = GenerateParserAutoId("i");
        var name = attrs["_pos0"] ?? "";
        var interfaceNameKey = "_pos0";
        var pos2 = "";

        if (string.IsNullOrEmpty(name))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "IFACE", "name");
        }

        // NEW: Parse optional type parameters §IFACE{...}<T, U>
        var typeParameters = ParseOptionalTypeParameterList(
            startToken.Span,
            allowVariance: true,
            owner: "an interface");
        ConsumeCanonicalDeclarationModifiers(startToken, attrs);
        var visibility = ParseVisibility(attrs["_pos1"], startToken.Span);
        if (attrs["_pos2"] != null)
            _diagnostics.ReportError(startToken.Span, DiagnosticCode.InvalidModifier,
                "Unsupported interface header modifier.");

        // Legacy: Extract type parameters from name if present (e.g., IProducer<out T>)
        if (typeParameters.Count == 0)
        {
            var typeParamStart = name.IndexOf('<');
            if (typeParamStart >= 0)
            {
                var typeParamEnd = name.LastIndexOf('>');
                if (typeParamEnd > typeParamStart)
                {
                    var typeParamStr = name.Substring(typeParamStart + 1, typeParamEnd - typeParamStart - 1);
                    name = name.Substring(0, typeParamStart);

                    foreach (var tpName in typeParamStr.Split(','))
                    {
                        var trimmed = tpName.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                        {
                            var variance = Ast.VarianceKind.None;
                            if (trimmed.StartsWith("out "))
                            {
                                variance = Ast.VarianceKind.Out;
                                trimmed = trimmed.Substring(4);
                            }
                            else if (trimmed.StartsWith("in "))
                            {
                                variance = Ast.VarianceKind.In;
                                trimmed = trimmed.Substring(3);
                            }
                            typeParameters.Add(new TypeParameterNode(startToken.Span, trimmed, Array.Empty<TypeConstraintNode>(), variance));
                        }
                    }
                }
            }
        }

        var baseInterfaces = new List<string>();
        var baseInterfaceSpans = new List<TextSpan>();
        if (!string.IsNullOrEmpty(pos2))
        {
            foreach (var baseIface in pos2.Split(','))
            {
                var trimmed = baseIface.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    baseInterfaces.Add(trimmed);
                }
            }
            if (attrs.GetSpan("_pos1") is { } baseInterfaceSpan)
                baseInterfaceSpans.Add(baseInterfaceSpan);
        }
        var methods = new List<MethodSignatureNode>();
        var properties = new List<PropertyNode>();
        var indexers = new List<IndexerNode>();
        var interopBlocks = new List<CSharpInteropBlockNode>();
        var preprocessorBlocks = new List<MemberPreprocessorBlockNode>();
        var interfaceItems = new List<AstNode>();

        // An EMPTY interface body (a marker interface) followed by a sibling type
        // at the SAME COLUMN as the §IFACE produces no Dedent, so the sibling's
        // opener is the next token. Interfaces cannot nest types, so a type opener
        // at that exact column can only be a sibling: stop and let the enclosing
        // scope parse it (#903 cluster 2, Calor0100 "Expected EXT, METHOD, PROP,
        // IXER, or END_IFACE"). An INDENTED type opener is NOT a sibling and still
        // reports Calor0100 - review M2.
        while (!IsAtEnd
               && !IsBlockEnd(TokenKind.EndInterface)
               && !IsSiblingTypeOpenerAt(startToken.Span.Column))
        {
            if (Check(TokenKind.Where))
            {
                ParseWhereClause(typeParameters);
            }
            else if (Check(TokenKind.Extends))
            {
                var extends = Expect(TokenKind.Extends);
                var typeStart = _position;
                baseInterfaces.Add(ReadInlineTypeToken());
                baseInterfaceSpans.Add(GetConsumedSpan(typeStart));
                continue;
            }
            else if (Check(TokenKind.Func))
            {
                var node = ParseMethodSignature();
                methods.Add(node);
                interfaceItems.Add(node);
            }
            else if (Check(TokenKind.Property))
            {
                var node = ParseProperty();
                properties.Add(node);
                interfaceItems.Add(node);
            }
            else if (Check(TokenKind.This))
            {
                var node = ParseCanonicalIndexer();
                indexers.Add(node);
                interfaceItems.Add(node);
            }
            else if (Check(TokenKind.CSharpInterop))
            {
                var node = ParseCSharpInteropBlock();
                interopBlocks.Add(node);
                interfaceItems.Add(node);
            }
            else if (Check(TokenKind.CompilerDirective))
            {
                interfaceItems.Add(ParseCompilerDirective());
            }
            else if (Check(TokenKind.Preprocessor))
            {
                var node = ParseMemberPreprocessorBlock(interfaceMembers: true);
                preprocessorBlocks.Add(node);
                interfaceItems.Add(node);
            }
            else
            {
                _diagnostics.ReportUnexpectedToken(Current.Span, "EXT, METHOD, PROP, IXER, or END_IFACE", Current.Kind);
                Advance();
            }
        }

        Token endToken;
        if (IsSiblingTypeOpenerAt(startToken.Span.Column))
        {
            endToken = Peek(-1);
        }
        else
        {
            endToken = ExpectBlockEnd(TokenKind.EndInterface);
            var endAttrs = ParseAttributes();

        }

        var span = startToken.Span.Union(endToken.Span);
        return WithDocumentation(new InterfaceDefinitionNode(span, id, name, baseInterfaces, typeParameters, methods, properties, attrs, csharpAttrs,
            indexers: indexers.Count > 0 ? indexers : null,
            baseInterfaceSpans: baseInterfaceSpans,
            identifierSpan: GetIdentifierSpan(attrs, interfaceNameKey, name),
            interopBlocks: interopBlocks,
            preprocessorBlocks: preprocessorBlocks,
            items: preprocessorBlocks.Count > 0
                || interfaceItems.Any(item => item is CompilerDirectiveNode)
                || interopBlocks.Any(block =>
                    block.CSharpCode.TrimStart().StartsWith("#", StringComparison.Ordinal))
                    ? interfaceItems
                    : null,
            visibility: visibility), startToken);
    }

    /// <summary>
    /// Parses a method signature (for interfaces).
    /// §METHOD{m001:Area}&lt;T&gt;[@Obsolete] §O{f64} §/METHOD{m001}
    /// </summary>
    private MethodSignatureNode ParseMethodSignature()
    {
        var startToken = Check(TokenKind.Func) ? Advance() : Expect(TokenKind.Method);
        var attrs = ParseDeclarationAttributes(startToken);
        ValidateIdentityFreeHeader(startToken, attrs, 2);
        var csharpAttrs = ParseCSharpAttributes();

        // Positional: [id:name]
        var id = GenerateParserAutoId("mt");
        var name = attrs["_pos0"] ?? "";

        // NEW: Parse optional type parameters §MT{...}<T, U>
        var effectParameters = new List<EffectParameterInfo>();
        var typeParameters = ParseOptionalTypeParameterList(
            startToken.Span,
            effectParameters: effectParameters);
        ConsumeCanonicalDeclarationModifiers(startToken, attrs);
        var visStr = attrs["_pos1"];
        if (!string.IsNullOrEmpty(visStr)
            && ParseVisibility(visStr, startToken.Span) is not Visibility.Public
            && TryParseVisibilityWord(visStr, out _))
        {
            _diagnostics.ReportError(startToken.Span, DiagnosticCode.UnknownVisibility,
                $"Interface method '{name}' must be public.");
        }
        if (attrs["_pos2"] != null)
            _diagnostics.ReportError(startToken.Span, DiagnosticCode.InvalidModifier,
                "Unsupported interface method header modifier.");
        // §7.3 position 1: this declaration binds its own effect variables, and they
        // stay in scope for its own row and its parameters' rows until it is parsed out.
        using var effectScope = PushEffectVariableScope(effectParameters);

        // --- Compact syntax: inline signature ---
        var parameters = new List<ParameterNode>();
        OutputNode? output = null;
        TryParseInlineSignature(startToken.Span, parameters, ref output);
        EffectsNode? effects = null;
        var preconditions = new List<RequiresNode>();
        var postconditions = new List<EnsuresNode>();

        // Parse signature elements until END_METHOD
        var bodyHadContent = false;
        while (!IsAtEnd && !IsMemberBlockEnd(TokenKind.EndMethod) && !IsModuleFunctionSibling(startToken))
        {
            if (Check(TokenKind.Where))
            {
                ParseWhereClause(typeParameters);
                bodyHadContent = true;
            }
            else if (Check(TokenKind.In))
            {
                parameters.Add(ParseParameter());
                bodyHadContent = true;
            }
            else if (Check(TokenKind.Out))
            {
                output = ParseOutput();
                bodyHadContent = true;
            }
            else if (Check(TokenKind.Exclamation))
            {
                effects = ParseEffects();
                bodyHadContent = true;
            }
            else if (Check(TokenKind.Requires))
            {
                preconditions.Add(ParseRequires());
                bodyHadContent = true;
            }
            else if (Check(TokenKind.FatArrow))
            {
                postconditions.Add(ParseEnsures());
                bodyHadContent = true;
            }
            else
            {
                break;
            }
        }

        Token endToken;
        if (Check(TokenKind.CSharpInterop) || Check(TokenKind.Preprocessor))
        {
            endToken = startToken;
        }
        else if (TryExpectMemberBlockEnd(TokenKind.EndMethod, startToken, bodyHadContent, out endToken))
        {
            var endAttrs = ParseAttributes();

        }

        var span = startToken.Span.Union(endToken.Span);
        return WithDocumentation(new MethodSignatureNode(span, id, name, typeParameters, parameters, output, effects, preconditions, postconditions, attrs, csharpAttrs)
        {
            IdentifierSpan = GetIdentifierSpan(attrs, "_pos0", name),
            EffectParameters = effectParameters,
        }, startToken);
    }

    /// <summary>
    /// Parses a class definition.
    /// §CLASS[c001:Shape:abs]
    ///   §IMPL[IShape]
    ///   §FLD[string:Name:pri]
    ///   §METHOD[m001:Area:pub:abs] §O[f64] §E[] §/METHOD[m001]
    /// §/CLASS[c001]
    /// </summary>
    private bool _parsingModuleMembers;

    private ClassDefinitionNode ParseClassDefinition()
    {
        var previous = _parsingModuleMembers;
        _parsingModuleMembers = Check(TokenKind.Module);
        try
        {
            return ParseTypeMemberDeclaration();
        }
        finally
        {
            _parsingModuleMembers = previous;
        }
    }

    private ClassDefinitionNode ParseTypeMemberDeclaration()
    {
        var isModule = _parsingModuleMembers;
        var isRecord = Check(TokenKind.Record);
        var startToken = Expect(isModule ? TokenKind.Module : isRecord ? TokenKind.Record : TokenKind.Class);
        var closer = isModule ? TokenKind.EndModule : TokenKind.EndClass;
        var attrs = new AttributeCollection();
        List<TypeParameterNode>? recordTypeParameters = null;
        attrs = ParseDeclarationAttributes(startToken);
        recordTypeParameters = ParseOptionalTypeParameterList(startToken.Span, effectOwner: "a type");
        ConsumeCanonicalDeclarationModifiers(startToken, attrs);
        ValidateIdentityFreeHeader(startToken, attrs, 3);
        var csharpAttrs = ParseCSharpAttributes();

        // Positional: [id:name:modifiers?] or [id:name:baseClass:modifiers?]
        var id = GenerateParserAutoId("c");
        var name = attrs["_pos0"] ?? "";
        var classNameKey = "_pos0";
        var pos2 = attrs["_pos1"] ?? "";
        var pos3 = attrs["_pos2"];

        if (string.IsNullOrEmpty(name))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "CLASS", "name");
        }

        // Disambiguate positionals:
        // 4 positionals (pos3 exists): pos2 is visibility, pos3 is modifiers -- OR pos2 is baseClass, pos3 is modifiers
        // 3 positionals (pos3 null): if pos2 is all known modifiers/visibility → modifiers; else baseClass
        string modifiers;
        string? baseClass = null;
        TextSpan? baseClassSpan = null;
        Visibility visibility = Visibility.Public;

        foreach (var word in pos2.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
        }
        visibility = ParseVisibility(attrs["_pos1"], startToken.Span);
        modifiers = attrs["_pos2"] ?? "";

        // Token-based matching: split modifiers and check each token against known abbreviations
        var modifierTokens = modifiers.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in modifierTokens)
        {
            if (!ClassModifierKeywords.Contains(token))
            {
                var lower = token.ToLowerInvariant();
                var hint = (RetiredModifierReplacement(token) ?? RetiredModifierReplacement(lower)) is { } replacement
                    ? $"Use '{replacement}' instead."
                    : RemovedVisibilityReplacement(token) is { } visibilityWord
                    ? $"Use '{visibilityWord}' instead."
                    : ClassModifierKeywords.Contains(lower)
                    ? $"Use '{lower}' instead."
                    : "Use a supported class modifier such as 'abstract' or 'struct'; static containers use a named module.";
                _diagnostics.ReportError(startToken.Span, DiagnosticCode.InvalidModifier,
                    $"Unknown class modifier '{token}'. Modifiers are case-sensitive. {hint}");
            }
        }
        var isAbstract = modifierTokens.Any(t => t.Equals("abstract", StringComparison.Ordinal));
        var isSealed = modifierTokens.Any(t => t.Equals("sealed", StringComparison.Ordinal));
        var isStatic = isModule || modifierTokens.Any(t => t.Equals("static", StringComparison.Ordinal));
        if (!isModule && isStatic)
        {
            _diagnostics.ReportError(startToken.Span, DiagnosticCode.InvalidModifier,
                $"Static class declarations are removed. Use a named 'module {name}' inside the compilation-unit wrapper, with 'func' members. Static members of non-static classes remain supported.");
        }
        var isPartial = modifierTokens.Any(t => t.Equals("partial", StringComparison.Ordinal));
        var isStruct = modifierTokens.Any(t => t.Equals("struct", StringComparison.Ordinal));
        var isReadOnly = modifierTokens.Any(t => t.Equals("readonly", StringComparison.Ordinal));
        if (isRecord && (isStruct || isStatic || isReadOnly))
        {
            _diagnostics.ReportError(startToken.Span, DiagnosticCode.InvalidModifier,
                "A record declaration represents a C# record class, not a struct or static container.");
            isStruct = false;
            isStatic = false;
            isReadOnly = false;
        }

        // Structs cannot be abstract
        if (isStruct && isAbstract)
        {
            _diagnostics.ReportError(startToken.Span, DiagnosticCode.InvalidModifier,
                "Structs cannot be abstract. The 'abstract' modifier will be ignored.");
            isAbstract = false;
        }
        var implementedInterfaces = new List<string>();
        var implementedInterfaceSpans = new List<TextSpan>();

        // NEW: Parse optional type parameters §CL{...}<T, U>
        var typeParameters = recordTypeParameters ?? ParseOptionalTypeParameterList(
            startToken.Span,
            effectOwner: "a class");

        List<ParameterNode>? recordParameters = null;
        List<ExpressionNode>? recordBaseArguments = null;
        if (isRecord && Check(TokenKind.OpenParen))
        {
            recordParameters = [];
            OutputNode? recordOutput = null;
            TryParseInlineSignature(startToken.Span, recordParameters, ref recordOutput);
            if (recordOutput != null || recordParameters.Any(parameter => parameter.Modifier != ParameterModifier.None))
                _diagnostics.ReportError(startToken.Span, DiagnosticCode.InvalidModifier,
                    "Record positional parameters have no return type or parameter modifiers.");
        }
        if (isRecord && Match(TokenKind.Colon))
        {
            var baseStart = _position;
            baseClass = ReadInlineTypeToken();
            baseClassSpan = GetConsumedSpan(baseStart);
            recordBaseArguments = ParseRecordBaseArguments();
        }

        // Also support legacy: Extract type parameters from class name if present (e.g., Repository<T>)
        if (typeParameters.Count == 0)
        {
            var typeParamStart = name.IndexOf('<');
            if (typeParamStart >= 0)
            {
                var typeParamEnd = name.LastIndexOf('>');
                if (typeParamEnd > typeParamStart)
                {
                    var typeParamStr = name.Substring(typeParamStart + 1, typeParamEnd - typeParamStart - 1);
                    name = name.Substring(0, typeParamStart);

                    // Parse type parameter names (comma-separated), with optional variance
                    foreach (var tpName in typeParamStr.Split(','))
                    {
                        var trimmedName = tpName.Trim();
                        if (!string.IsNullOrEmpty(trimmedName))
                        {
                            var variance = Ast.VarianceKind.None;
                            if (trimmedName.StartsWith("out "))
                            {
                                variance = Ast.VarianceKind.Out;
                                trimmedName = trimmedName.Substring(4);
                            }
                            else if (trimmedName.StartsWith("in "))
                            {
                                variance = Ast.VarianceKind.In;
                                trimmedName = trimmedName.Substring(3);
                            }
                            if (variance != Ast.VarianceKind.None)
                            {
                                _diagnostics.ReportError(
                                    startToken.Span,
                                    DiagnosticCode.InvalidTypeParameterVariance,
                                    "Type parameter variance is only legal on interfaces and delegates, not a class.");
                            }
                            typeParameters.Add(new TypeParameterNode(startToken.Span, trimmedName, Array.Empty<TypeConstraintNode>(), variance));
                        }
                    }
                }
            }
        }
        var fields = new List<ClassFieldNode>();
        var properties = new List<PropertyNode>();
        var indexers = new List<IndexerNode>();
        var constructors = new List<ConstructorNode>();
        var methods = new List<MethodNode>();
        var events = new List<EventDefinitionNode>();
        var operatorOverloads = new List<OperatorOverloadNode>();
        var interopBlocks = new List<CSharpInteropBlockNode>();
        var preprocessorBlocks = new List<MemberPreprocessorBlockNode>();
        var nestedClasses = new List<ClassDefinitionNode>();
        var nestedInterfaces = new List<InterfaceDefinitionNode>();
        var nestedEnums = new List<EnumDefinitionNode>();
        var nestedDelegates = new List<DelegateDefinitionNode>();
        var classItems = new List<AstNode>();
        EffectsNode? staticInitializerEffects = null;

        // #1485: a class owns only the lines indented under it, so an empty
        // class does not adopt a same-column sibling type as a nested type.
        var bodyOwner = BeginBlockBody(startToken, closer, id);
        while (!IsAtEnd
               && !IsBlockEnd(closer)
               && IsInDeclarationBody(bodyOwner, interopIsSibling: true))
        {
            if (Check(TokenKind.Where))
            {
                ParseWhereClause(typeParameters);
            }
            else if (Check(TokenKind.Exclamation))
            {
                // 0.30 (D1): a row among a type's header clauses is the row of its implicit
                // static initialization (static field and property initializers without an
                // explicit static constructor). Only the header position carries it.
                var rowToken = Current;
                var row = ParseEffects();
                if (classItems.Count > 0)
                    _diagnostics.ReportError(rowToken.Span, DiagnosticCode.EffectRowMisplaced,
                        "A type's static-initialization row is a header clause: write it before the first member.");
                else if (staticInitializerEffects != null)
                    _diagnostics.ReportError(rowToken.Span, DiagnosticCode.EffectRowMisplaced,
                        "A type carries one static-initialization row. Put every permission in that row.");
                else
                    staticInitializerEffects = row;
            }
            else if (Check(TokenKind.Extends))
            {
                var extends = Expect(TokenKind.Extends);
                var typeStart = _position;
                baseClass = ReadInlineTypeToken();
                baseClassSpan = GetConsumedSpan(typeStart);
                if (isRecord)
                    recordBaseArguments = ParseRecordBaseArguments();
            }
            else if (Check(TokenKind.Implements))
            {
                var implements = Expect(TokenKind.Implements);
                var implAttrs = new AttributeCollection();
                var typeStart = _position;
                implAttrs.Add("_pos0", ReadInlineTypeToken(), GetConsumedSpan(typeStart));
                var iface = implAttrs["_pos0"] ?? "";
                if (!string.IsNullOrEmpty(iface))
                {
                    implementedInterfaces.Add(iface);
                    if (implAttrs.GetSpan("_pos0") is { } interfaceSpan)
                        implementedInterfaceSpans.Add(interfaceSpan);
                }
            }
            else if (Check(TokenKind.FieldDef))
            {
                var node = ParseClassField();
                fields.Add(node);
                classItems.Add(node);
            }
            else if (Check(TokenKind.Property))
            {
                var node = ParseProperty();
                properties.Add(node);
                classItems.Add(node);
            }
            else if (Check(TokenKind.This))
            {
                var node = ParseCanonicalIndexer();
                indexers.Add(node);
                classItems.Add(node);
            }
            else if (Check(TokenKind.Constructor))
            {
                var node = ParseConstructor();
                constructors.Add(node);
                classItems.Add(node);
            }
            else if (Check(TokenKind.OperatorOverload))
            {
                var node = ParseOperatorOverload();
                if (node != null)
                {
                    operatorOverloads.Add(node);
                    classItems.Add(node);
                }
            }
            else if (Check(TokenKind.Func))
            {
                var node = ParseMethodDefinition();
                methods.Add(node);
                classItems.Add(node);
            }
            else if (Check(TokenKind.Event))
            {
                var node = ParseEventDefinition();
                events.Add(node);
                classItems.Add(node);
            }
            else if (Check(TokenKind.CSharpInterop))
            {
                var node = ParseCSharpInteropBlock();
                interopBlocks.Add(node);
                classItems.Add(node);
            }
            else if (Check(TokenKind.CompilerDirective))
            {
                classItems.Add(ParseCompilerDirective());
            }
            else if (Check(TokenKind.Preprocessor))
            {
                var node = ParseMemberPreprocessorBlock();
                preprocessorBlocks.Add(node);
                classItems.Add(node);
            }
            else if (Check(TokenKind.Class) || Check(TokenKind.Record) || Check(TokenKind.Module))
            {
                var node = ParseClassDefinition();
                nestedClasses.Add(node);
                classItems.Add(node);
            }
            else if (Check(TokenKind.Interface))
            {
                var node = ParseInterfaceDefinition();
                nestedInterfaces.Add(node);
                classItems.Add(node);
            }
            else if (Check(TokenKind.Enum))
            {
                var node = ParseEnumDefinition();
                nestedEnums.Add(node);
                classItems.Add(node);
            }
            else if (Check(TokenKind.Delegate))
            {
                var node = ParseDelegateDefinition();
                nestedDelegates.Add(node);
                classItems.Add(node);
            }
            else if (Check(TokenKind.Let) || Check(TokenKind.Var))
            {
                var bindingToken = Current;
                _ = ParseCanonicalBinding();
                _diagnostics.ReportError(bindingToken.Span, DiagnosticCode.UnexpectedToken,
                    $"'{bindingToken.Text}' is a local binding and cannot appear in a class body; " +
                    "declare a field or property with an inline initializer instead.");
            }
            else
            {
                _diagnostics.ReportUnexpectedToken(Current.Span, "TP, WHERE, EXT, IMPL, FLD, PROP, IXER, CTOR, OP, METHOD, AMT, EVT, CSHARP, PP, CLASS, IFACE, EN, DEL, or END_CLASS", Current.Kind);
                Advance();
            }
        }

        Token endToken;
        endToken = ExpectOwnedBlockEnd(closer, bodyOwner);
        var endAttrs = ParseAttributes();

        // 0.30 (D1): an explicit static constructor carries the type's initialization row.
        if (staticInitializerEffects != null && constructors.Any(constructor => constructor.IsStatic))
            _diagnostics.ReportError(staticInitializerEffects.Span, DiagnosticCode.EffectRowMisplaced,
                "This type has an explicit static constructor; declare the static-initialization row on 'ctor static' instead.");
        var span = startToken.Span.Union(endToken.Span);
        var identifierSpan = GetIdentifierSpan(attrs, classNameKey, name);
        return WithDocumentation(new ClassDefinitionNode(span, id, name, isAbstract, isSealed, isPartial, isStatic, baseClass,
            implementedInterfaces, typeParameters, fields, properties, constructors, methods, events, operatorOverloads, attrs, csharpAttrs,
            isStruct: isStruct, isReadOnly: isReadOnly, visibility: visibility, interopBlocks: interopBlocks,
            preprocessorBlocks: preprocessorBlocks.Count > 0 ? preprocessorBlocks : null,
            nestedClasses: nestedClasses.Count > 0 ? nestedClasses : null,
            nestedInterfaces: nestedInterfaces.Count > 0 ? nestedInterfaces : null,
            nestedEnums: nestedEnums.Count > 0 ? nestedEnums : null,
            indexers: indexers.Count > 0 ? indexers : null,
            nestedDelegates: nestedDelegates.Count > 0 ? nestedDelegates : null,
            identifierSpan: identifierSpan,
            baseClassSpan: baseClassSpan,
            implementedInterfaceSpans: implementedInterfaceSpans,
            items: classItems,
            isModule: isModule,
            isRecord: isRecord,
            recordParameters: recordParameters,
            recordBaseArguments: recordBaseArguments)
        {
            StaticInitializerEffects = staticInitializerEffects
        }, startToken);
    }

    private List<ExpressionNode>? ParseRecordBaseArguments()
    {
        if (!Match(TokenKind.OpenParen))
            return null;

        var arguments = new List<ExpressionNode>();
        if (!Check(TokenKind.CloseParen))
        {
            do { arguments.Add(ParseExpression()); }
            while (Match(TokenKind.Comma));
        }
        Expect(TokenKind.CloseParen);
        return arguments;
    }

    /// <summary>
    /// Parses a C# interop block.
    /// §CSHARP{...}§/CSHARP
    /// </summary>
    private CSharpInteropBlockNode ParseCSharpInteropBlock()
    {
        var token = Expect(TokenKind.CSharpInterop);
        if (token.Value is RawCSharpFrame frame)
        {
            return new CSharpInteropBlockNode(
                token.Span, frame.Code, frame.FeatureName, frame.Reason,
                frame.MemberKind, frame.IsCompilationUnitPassthrough);
        }
        var content = token.Value as string ?? "";
        var isCompilationUnitPassthrough = content.StartsWith(
            CSharpInteropBlockNode.CompilationUnitPassthroughMarker,
            StringComparison.Ordinal);
        if (isCompilationUnitPassthrough)
        {
            content = content[
                CSharpInteropBlockNode.CompilationUnitPassthroughMarker
                    .Length..];
        }
        return new CSharpInteropBlockNode(
            token.Span,
            content,
            isCompilationUnitPassthrough:
                isCompilationUnitPassthrough);
    }

    /// <summary>
    /// Parses a class field.
    /// §FLD[string:Name:pri][@JsonIgnore]
    /// </summary>
    private ClassFieldNode ParseClassField()
    {
        var startToken = Expect(TokenKind.FieldDef);
        var attrs = ParseTypedDeclarationAttributes(startToken, typeFirst: true);
        var csharpAttrs = ParseCSharpAttributes();

        // Positional: [type:name:visibility?:modifiers?]
        var typeName = attrs["_pos0"] ?? "object";
        var name = attrs["_pos1"] ?? "";
        var visStr = attrs["_pos2"] ?? ("public");
        var modStr = attrs["_pos3"] ?? "";

        if (string.IsNullOrEmpty(name))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "FLD", "name");
        }

        var visibility = ParseVisibility(visStr, startToken.Span);
        var fieldModifiers = ParseMethodModifiers(modStr, startToken.Span);
        if (_parsingModuleMembers && !fieldModifiers.HasFlag(MethodModifiers.Const))
            fieldModifiers |= MethodModifiers.Static;

        // Position 8 (§3.3): §FLD{Action<i32>:onChange:priv} §E{cw}. Read BEFORE the
        // default-value branch. X9b is the executed proof this did not already parse:
        // the class-member loop rejected Effects four diagnostics earlier.
        var row = TryParseSameLineRow(EffectRowPosition.Field);

        // Check for optional default value (can be prefixed with = or just a direct expression)
        ExpressionNode? defaultValue = null;
        if (Check(TokenKind.Equals))
        {
            Advance(); // consume =
            defaultValue = ParseExpression();
        }

        // §3.1 recovery for position 8. Executed baseline Z1: four Calor0100 today.
        TryRecoverTrailingEffectRow(name, "field");

        var span = defaultValue != null ? startToken.Span.Union(defaultValue.Span) : startToken.Span;
        return WithDocumentation(new ClassFieldNode(
            span,
            name,
            typeName,
            visibility,
            fieldModifiers,
            defaultValue,
            attrs,
            csharpAttrs,
            GetIdentifierSpan(attrs, "_pos1", name),
            attrs.GetSpan("_pos0"),
            row), startToken);
    }

    /// <summary>
    /// Parses a method definition (with body).
    /// §METHOD[m001:Area:pub:over][@HttpPost][@Authorize]
    ///   §O[f64] §E[]
    ///   §R §OP[kind=mul] 3.14159 §REF[name=Radius] §REF[name=Radius]
    /// §/METHOD[m001]
    /// </summary>
    private MethodNode ParseMethodDefinition()
    {
        var isModuleFunction = Check(TokenKind.Func);
        var startToken = Expect(isModuleFunction ? TokenKind.Func : TokenKind.Method);
        var (since, deprecated, breakingChanges) = ParseDocumentationMetadata(startToken);
        var closer = isModuleFunction ? TokenKind.EndFunc : TokenKind.EndMethod;
        _lastIfToken = null; // scope Calor0117 fixes to this method's §IFs
        var attrs = ParseDeclarationAttributes(startToken);
        ValidateIdentityFreeHeader(startToken, attrs, 3);
        var csharpAttrs = ParseCSharpAttributes();

        // Positional: [id:name:visibility?:modifiers?]
        var id = GenerateParserAutoId("mt");
        var name = attrs["_pos0"] ?? "";
        var methodNameKey = "_pos0";
        var visStr = attrs["_pos1"] ?? "private";
        var modStr = attrs["_pos2"] ?? "";


        // NEW: Parse optional type parameters §MT{...}<T, U>
        var effectParameters = new List<EffectParameterInfo>();
        var typeParameters = ParseOptionalTypeParameterList(
            startToken.Span,
            effectParameters: effectParameters);
        ConsumeCanonicalDeclarationModifiers(startToken, attrs);
        var visibility = ParseVisibility(attrs["_pos1"], startToken.Span);
        var methodModifiers = (attrs["_pos2"] ?? modStr).Split(',');
        var isAsync = methodModifiers.Contains("async");
        var modifiers = ParseMethodModifiers(string.Join(",", methodModifiers.Where(modifier => modifier != "async")), startToken.Span);
        if (_parsingModuleMembers)
            modifiers |= MethodModifiers.Static;
        // §7.3 position 1: this declaration binds its own effect variables, and they
        // stay in scope for its own row and its parameters' rows until it is parsed out.
        using var effectScope = PushEffectVariableScope(effectParameters);

        // Also support legacy: Extract type parameters from method name if present (e.g., Create<T>)
        if (typeParameters.Count == 0)
        {
            var typeParamStart = name.IndexOf('<');
            if (typeParamStart >= 0)
            {
                var typeParamEnd = name.LastIndexOf('>');
                if (typeParamEnd > typeParamStart)
                {
                    var typeParamStr = name.Substring(typeParamStart + 1, typeParamEnd - typeParamStart - 1);
                    name = name.Substring(0, typeParamStart);

                    foreach (var tpName in typeParamStr.Split(','))
                    {
                        var trimmedName = tpName.Trim();
                        if (!string.IsNullOrEmpty(trimmedName))
                        {
                            typeParameters.Add(new TypeParameterNode(startToken.Span, trimmedName, Array.Empty<TypeConstraintNode>()));
                        }
                    }
                }
            }
        }

        // --- Compact syntax: inline signature ---
        var parameters = new List<ParameterNode>();
        OutputNode? output = null;
        TryParseInlineSignature(startToken.Span, parameters, ref output);
        EffectsNode? effects = null;
        var preconditions = new List<RequiresNode>();
        var postconditions = new List<EnsuresNode>();
        var body = new List<StatementNode>();

        // Parse signature and body until END_METHOD
        var bodyHadContent = false;
        while (!IsAtEnd && !IsMemberBlockEnd(closer) && !IsModuleFunctionSibling(startToken))
        {
            if (Check(TokenKind.Where))
            {
                ParseWhereClause(typeParameters);
                bodyHadContent = true;
            }
            else if (Check(TokenKind.In))
            {
                parameters.Add(ParseParameter());
                bodyHadContent = true;
            }
            else if (Check(TokenKind.Out))
            {
                output = ParseOutput();
                bodyHadContent = true;
            }
            else if (Check(TokenKind.Exclamation))
            {
                if (body.Count != 0)
                    _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken,
                        "Effect clauses must precede the first method body statement.");
                effects = ParseEffects();
                bodyHadContent = true;
            }
            else if (Check(TokenKind.Requires))
            {
                preconditions.Add(ParseRequires());
                bodyHadContent = true;
            }
            else if (Check(TokenKind.FatArrow))
            {
                if (body.Count != 0)
                    _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken,
                        "Postcondition clauses must precede the first method body statement.");
                postconditions.Add(ParseEnsures());
                bodyHadContent = true;
            }
            else
            {
                // Must be body statements
                var stmt = ParseStatement();
                if (stmt != null)
                {
                    body.Add(stmt);
                    bodyHadContent = true;
                }
            }
        }

        if (TryExpectMemberBlockEnd(closer, startToken, bodyHadContent, out var endToken))
        {
            var endAttrs = ParseAttributes();

        }

        var span = startToken.Span.Union(endToken.Span);
        return WithDocumentation(new MethodNode(span, id, name, visibility, modifiers, typeParameters, parameters,
            output, effects, preconditions, postconditions, body, attrs, csharpAttrs, isAsync: isAsync,
            identifierSpan: GetIdentifierSpan(attrs, methodNameKey, name),
            since: since, deprecated: deprecated, breakingChanges: breakingChanges)
        {
            EffectParameters = effectParameters,
        }, startToken);
    }

    private ParameterModifier ParseParameterModifiers(string value, TextSpan span, bool allowSemantic = false)
    {
        var result = ParameterModifier.None;
        foreach (var token in value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var modifier = token switch
            {
                "this" => ParameterModifier.This,
                "ref" => ParameterModifier.Ref,
                "out" => ParameterModifier.Out,
                "in" => ParameterModifier.In,
                "params" => ParameterModifier.Params,
                _ => ParameterModifier.None
            };
            if (modifier == ParameterModifier.None
                && (!allowSemantic || token.ToLowerInvariant() is "this" or "ref" or "out" or "in" or "params"))
            {
                _diagnostics.ReportError(span, DiagnosticCode.InvalidModifier,
                    $"Unknown parameter modifier '{token}'. Use exact-case 'this', 'ref', 'out', 'in', or 'params'.");
            }
            result |= modifier;
        }
        return result;
    }

    private MethodModifiers ParseMethodModifiers(string modStr, TextSpan? span = null)
    {
        var mods = MethodModifiers.None;
        // Token-based matching: split on commas/spaces and check each token
        var tokens = modStr.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            if (token.Equals("virtual", StringComparison.Ordinal))
                mods |= MethodModifiers.Virtual;
            else if (token.Equals("override", StringComparison.Ordinal))
                mods |= MethodModifiers.Override;
            else if (token.Equals("abstract", StringComparison.Ordinal))
                mods |= MethodModifiers.Abstract;
            else if (token.Equals("sealed", StringComparison.Ordinal))
                mods |= MethodModifiers.Sealed;
            else if (token.Equals("static", StringComparison.Ordinal))
                mods |= MethodModifiers.Static;
            else if (token.Equals("const", StringComparison.Ordinal))
                mods |= MethodModifiers.Const;
            else if (token.Equals("readonly", StringComparison.Ordinal))
                mods |= MethodModifiers.Readonly;
            else if (token.Equals("required", StringComparison.Ordinal))
                mods |= MethodModifiers.Required;
            else if (token.Equals("partial", StringComparison.Ordinal))
                mods |= MethodModifiers.Partial;
            else if (token.Equals("extern", StringComparison.Ordinal))
                mods |= MethodModifiers.Extern;
            else if (token.Equals("unsafe", StringComparison.Ordinal))
                mods |= MethodModifiers.Unsafe;
            else if (token.Equals("volatile", StringComparison.Ordinal))
                mods |= MethodModifiers.Volatile;
            else if (span is { } location)
            {
                var lower = token.ToLowerInvariant();
                var hint = (RetiredModifierReplacement(token) ?? RetiredModifierReplacement(lower)) is { } replacement
                    ? $"Use '{replacement}' instead."
                    : token != lower && ParseMethodModifiers(lower) != MethodModifiers.None
                    ? $"Use '{lower}' instead."
                    : "Use a C# member modifier such as 'static', 'virtual' or 'override'.";
                _diagnostics.ReportError(location, DiagnosticCode.InvalidModifier,
                    $"Unknown member modifier '{token}'. Modifiers are case-sensitive. {hint}");
            }
        }
        return mods;
    }

    /// <summary>
    /// 0.30 (#1596): modifiers are spelled as in C#. The old abbreviations are
    /// errors that name their replacement.
    /// </summary>
    private static string? RetiredModifierReplacement(string word) => word switch
    {
        "vr" or "virt" => "virtual",
        "ov" or "over" => "override",
        "abs" => "abstract",
        "seal" => "sealed",
        "st" or "stat" => "static",
        "req" => "required",
        "part" => "partial",
        "ext" => "extern",
        "vol" => "volatile",
        _ => null,
    };

    /// <summary>
    /// Parses native constructor and array creation.
    /// </summary>
    private ExpressionNode ParseNewExpression()
    {
        var start = Expect(TokenKind.New);
        if (Check(TokenKind.OpenBrace))
            return ParseNativeAnonymousObjectCreation(start);
        var type = ParseLispTypeName(out var typeSpan);
        if (SyntaxFactory.ParseTypeName(type) is ArrayTypeSyntax arrayType)
        {
            var rank = arrayType.RankSpecifiers[0].Sizes.Count;
            var element = arrayType.RankSpecifiers.Count == 1
                ? arrayType.ElementType.ToString()
                : arrayType.WithRankSpecifiers(SyntaxFactory.List(arrayType.RankSpecifiers.Skip(1))).ToString();
            return ParseNativeArrayCreation(start, element, typeSpan, rank, []);
        }
        if (Match(TokenKind.OpenBracket))
        {
            var sizes = new List<ExpressionNode>();
            while (!IsAtEnd && !Check(TokenKind.CloseBracket))
            {
                sizes.Add(ParseExpression());
                if (!Match(TokenKind.Comma))
                    break;
                if (Check(TokenKind.CloseBracket))
                    _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken,
                        "A sized array creation requires a size after every comma.");
            }
            Expect(TokenKind.CloseBracket);
            var elementType = new System.Text.StringBuilder(type);
            ReadInlineTypeSuffixes(elementType, allowNullable: false);
            return ParseNativeArrayCreation(start, elementType.ToString(), typeSpan, Math.Max(1, sizes.Count), sizes);
        }
        var arguments = new List<ExpressionNode>();
        var names = new List<string?>();
        if (Match(TokenKind.OpenParen))
        {
            while (!IsAtEnd && !Check(TokenKind.CloseParen))
            {
                string? name = null;
                if (Check(TokenKind.Identifier) && Peek(1).Kind == TokenKind.Colon)
                {
                    name = TokenIdentifier(Advance());
                    Advance();
                }
                arguments.Add(ParseExpression());
                names.Add(name);
                if (!Match(TokenKind.Comma))
                    break;
            }
            Expect(TokenKind.CloseParen);
        }
        else if (!Check(TokenKind.OpenBrace))
        {
            _diagnostics.ReportUnexpectedToken(Current.Span, "constructor argument list", Current.Kind);
        }
        var initializers = new List<ObjectInitializerAssignment>();
        var collectionInitializers = new List<CallExpressionNode>();
        if (Match(TokenKind.OpenBrace))
        {
            while (!IsAtEnd && !Check(TokenKind.CloseBrace))
            {
                if (Check(TokenKind.Identifier) && Peek(1).Kind == TokenKind.Equals)
                {
                    if (collectionInitializers.Count > 0)
                        _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken,
                            "Object and collection initializer entries cannot be mixed.");
                    var property = TokenIdentifier(Advance());
                    Advance();
                    initializers.Add(new ObjectInitializerAssignment(property, ParseExpression()));
                }
                else
                {
                    if (initializers.Count > 0)
                        _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken,
                            "Object and collection initializer entries cannot be mixed.");
                    var elementStart = Current.Span;
                    var elements = new List<ExpressionNode>();
                    if (Match(TokenKind.OpenBrace))
                    {
                        if (Check(TokenKind.CloseBrace))
                            _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken,
                                "A collection initializer entry requires at least one argument.");
                        while (!IsAtEnd && !Check(TokenKind.CloseBrace))
                        {
                            elements.Add(ParseExpression());
                            if (!Match(TokenKind.Comma))
                                break;
                            if (Check(TokenKind.CloseBrace))
                                _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken,
                                    "A grouped collection initializer entry cannot end with a comma.");
                        }
                        Expect(TokenKind.CloseBrace);
                    }
                    else
                        elements.Add(ParseExpression());
                    collectionInitializers.Add(new CallExpressionNode(
                        elementStart.Union(PreviousToken.Span), $"{type}.Add", elements,
                        null, null, null, calleeSpan: TextSpan.Empty));
                }
                if (!Match(TokenKind.Comma))
                    break;
            }
            Expect(TokenKind.CloseBrace);
        }
        var typeReference = ParseTypeReference(typeSpan);
        return new NewExpressionNode(start.Span.Union(PreviousToken.Span), type, [], arguments,
            collectionInitializers.Count == 0 ? initializers : [],
            typeReference?.Span ?? typeSpan, typeReference,
            argumentNames: names.Any(name => name is not null) ? names : null,
            collectionInitializers: collectionInitializers);
    }

    private AnonymousObjectCreationNode ParseNativeAnonymousObjectCreation(Token start)
    {
        Expect(TokenKind.OpenBrace);
        var initializers = new List<ObjectInitializerAssignment>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        while (!IsAtEnd && !Check(TokenKind.CloseBrace))
        {
            string? name = null;
            var memberStart = Current;
            if (Check(TokenKind.Identifier) && Peek(1).Kind == TokenKind.Equals)
            {
                name = TokenIdentifier(Advance());
                Advance();
            }
            var value = ParseExpression();
            if (name == null)
                name = value switch
                {
                    ReferenceNode reference when memberStart.Kind == TokenKind.Identifier =>
                        reference.Name[(reference.Name.LastIndexOf('.') + 1)..].TrimStart('@'),
                    FieldAccessNode field => field.FieldName.TrimStart('@'),
                    _ => null
                };
            if (name == null)
                _diagnostics.ReportError(memberStart.Span, DiagnosticCode.UnexpectedToken,
                    "An anonymous object member requires a name assignment or an identifier/member reference.");
            else if (!names.Add(name))
                _diagnostics.ReportError(memberStart.Span, DiagnosticCode.UnexpectedToken,
                    $"Anonymous object member '{name}' is declared more than once.");
            else
                initializers.Add(new ObjectInitializerAssignment(name, value));
            if (!Match(TokenKind.Comma))
                break;
        }
        var close = Expect(TokenKind.CloseBrace);
        return new AnonymousObjectCreationNode(start.Span.Union(close.Span), initializers);
    }

    private ExpressionNode ParseNativeArrayCreation(Token start, string elementType, TextSpan typeSpan,
        int rank, IReadOnlyList<ExpressionNode> sizes)
    {
        var elements = new List<ExpressionNode>();
        var rows = new List<IReadOnlyList<ExpressionNode>>();
        var nesting = new List<int>();
        var hasInitializer = Match(TokenKind.OpenBrace);
        if (hasInitializer)
        {
            if (rank > 1)
                ParseLevel(0);
            else
            {
                while (!IsAtEnd && !Check(TokenKind.CloseBrace))
                {
                    elements.Add(ParseExpression());
                    if (!Match(TokenKind.Comma))
                        break;
                }
                Expect(TokenKind.CloseBrace);
            }
        }
        else if (sizes.Count == 0)
            _diagnostics.ReportError(start.Span.Union(typeSpan), DiagnosticCode.UnexpectedToken,
                "An array creation requires dimension sizes or an initializer.");
        var span = start.Span.Union(PreviousToken.Span);
        var id = GenerateParserAutoId("a");
        return rank == 1
            ? new ArrayCreationNode(span, id, "", elementType, sizes.FirstOrDefault(), elements,
                new AttributeCollection(), typeSpan)
            : new MultiDimArrayCreationNode(span, id, "", elementType, rank, sizes, rows, typeSpan,
                hasInitializer ? nesting : null);

        void ParseLevel(int level)
        {
            var position = nesting.Count;
            nesting.Add(0);
            var row = new List<ExpressionNode>();
            while (!IsAtEnd && !Check(TokenKind.CloseBrace))
            {
                if (level == rank - 1)
                    row.Add(ParseExpression());
                else
                {
                    Expect(TokenKind.OpenBrace);
                    ParseLevel(level + 1);
                }
                nesting[position]++;
                if (!Match(TokenKind.Comma))
                    break;
            }
            Expect(TokenKind.CloseBrace);
            if (level == rank - 1)
                rows.Add(row);
        }
    }

    private TypeReferenceNode? ParseTypeReference(TextSpan span)
    {
        if (span.Length <= 0)
            return null;

        var tokens = _tokens
            .Where(token =>
                !token.IsTrivia
                && token.Kind is not TokenKind.Indent and not TokenKind.Dedent
                && token.Span.Start >= span.Start
                && token.Span.End <= span.End)
            .ToArray();
        return new TypeReferenceTokenReader(tokens).Parse();
    }

    private sealed class TypeReferenceTokenReader
    {
        private readonly IReadOnlyList<Token> _tokens;
        private int _position;
        private int _pendingGreaterClosers;

        public TypeReferenceTokenReader(IReadOnlyList<Token> tokens)
        {
            _tokens = tokens;
        }

        public TypeReferenceNode? Parse()
        {
            while (Check(TokenKind.Question))
                Advance();

            if (Check(TokenKind.OpenBracket))
            {
                while (Check(TokenKind.OpenBracket))
                    Advance();
                var elementType = Parse();
                while (Check(TokenKind.CloseBracket))
                    Advance();
                return elementType;
            }

            if (Check(TokenKind.OpenParen))
                return ParseTupleType();

            if (!IsTypeNameToken(Current))
                return null;

            var name = new System.Text.StringBuilder();
            var nameToken = Advance();
            name.Append(nameToken.Text);
            while (TryConsumeQualifiedSeparator(out var separator))
            {
                if (!IsTypeNameToken(Current))
                    break;

                name.Append(separator);
                nameToken = Advance();
                name.Append(nameToken.Text);
            }

            var typeArguments = new List<TypeReferenceNode>();
            if (Check(TokenKind.Less))
            {
                Advance();
                while (!IsAtEnd && !IsGreater())
                {
                    var typeArgument = Parse();
                    if (typeArgument != null)
                        typeArguments.Add(typeArgument);
                    else
                        Advance();

                    if (Check(TokenKind.Comma))
                        Advance();
                    else if (!IsGreater() && typeArgument == null)
                        continue;
                }

                ConsumeGreater();
            }

            while (Check(TokenKind.Question)
                   || Check(TokenKind.Star)
                   || Check(TokenKind.StarStar))
            {
                Advance();
            }
            while (Check(TokenKind.OpenBracket))
            {
                Advance();
                while (Check(TokenKind.Comma))
                    Advance();
                if (Check(TokenKind.CloseBracket))
                    Advance();
            }

            return new TypeReferenceNode(nameToken.Span, name.ToString(), typeArguments);
        }

        private TypeReferenceNode ParseTupleType()
        {
            Advance();
            var elements = new List<TypeReferenceNode>();
            while (!IsAtEnd && !Check(TokenKind.CloseParen))
            {
                var element = Parse();
                if (element != null)
                    elements.Add(element);
                else
                    Advance();

                if (Check(TokenKind.Comma))
                    Advance();
            }
            if (Check(TokenKind.CloseParen))
                Advance();

            return new TypeReferenceNode(TextSpan.Empty, string.Empty, elements);
        }

        private bool TryConsumeQualifiedSeparator(out string separator)
        {
            if (Check(TokenKind.Dot))
            {
                Advance();
                separator = ".";
                return true;
            }

            if (Check(TokenKind.Colon) && Peek(1).Kind == TokenKind.Colon)
            {
                Advance();
                Advance();
                separator = "::";
                return true;
            }

            separator = string.Empty;
            return false;
        }

        private bool IsGreater() =>
            _pendingGreaterClosers > 0
            || Check(TokenKind.Greater)
            || Check(TokenKind.GreaterGreater);

        private void ConsumeGreater()
        {
            if (_pendingGreaterClosers > 0)
            {
                _pendingGreaterClosers--;
                return;
            }

            if (Check(TokenKind.Greater))
            {
                Advance();
            }
            else if (Check(TokenKind.GreaterGreater))
            {
                Advance();
                _pendingGreaterClosers = 1;
            }
        }

        private bool IsAtEnd => _pendingGreaterClosers == 0 && _position >= _tokens.Count;

        private Token Current =>
            _pendingGreaterClosers > 0 || _position >= _tokens.Count
                ? default
                : _tokens[_position];

        private Token Peek(int offset)
        {
            if (_pendingGreaterClosers > 0)
                return default;
            var index = _position + offset;
            return index >= 0 && index < _tokens.Count ? _tokens[index] : default;
        }

        private bool Check(TokenKind kind) =>
            _pendingGreaterClosers == 0
            && _position < _tokens.Count
            && _tokens[_position].Kind == kind;

        private Token Advance()
        {
            if (_pendingGreaterClosers > 0 || _position >= _tokens.Count)
                return default;
            return _tokens[_position++];
        }

        private static bool IsTypeNameToken(Token token) =>
            token.Kind == TokenKind.Identifier || token.IsKeyword;
    }

    /// <summary>
    /// Parses a 'this' expression with optional member access.
    /// §THIS or §THIS.property
    /// </summary>
    private ExpressionNode ParseThisExpression()
    {
        var token = Expect(TokenKind.This);
        ExpressionNode expr = new ThisExpressionNode(token.Span);
        return ParseTrailingMemberAccess(expr);
    }

    /// <summary>
    /// Parses a 'base' expression with optional member access.
    /// §BASE or §BASE.property
    /// </summary>
    private ExpressionNode ParseBaseExpression()
    {
        var token = Expect(TokenKind.Base);
        ExpressionNode expr = new BaseExpressionNode(token.Span);
        return ParseTrailingMemberAccess(expr);
    }

    // Phase 9: Properties and Constructors

    /// <summary>
    /// Parses a property definition.
    /// §PROP[p001:Name:string:pub][@JsonProperty("name")]
    ///   §GET
    ///   §SET[pri]
    /// §/PROP[p001]
    /// </summary>
    private PropertyNode ParseProperty()
    {
        var startToken = Expect(TokenKind.Property);
        var attrs = ParseTypedDeclarationAttributes(startToken, typeFirst: false);
        ValidateIdentityFreeHeader(startToken, attrs, 5);
        var csharpAttrs = ParseCSharpAttributes();

        // Positional: [id:name:type:visibility?:modifiers?:accessors?]
        var id = GenerateParserAutoId("p");
        var name = attrs["_pos0"] ?? "";
        var propertyNameKey = "_pos0";
        var propertyTypeKey = "_pos1";
        var typeName = attrs["_pos1"] ?? "object";
        var visStr = attrs["_pos2"] ?? "public";
        var modStr = attrs["_pos3"] ?? "";
        var accessorStr = attrs["_pos4"] ?? "";  // 6th positional for accessor shorthand

        if (string.IsNullOrEmpty(accessorStr) && IsAccessorShorthand(modStr))
        {
            accessorStr = modStr;
            modStr = "";
        }


        var visibility = ParseVisibility(visStr, startToken.Span);
        var modifiers = ParseMethodModifiers(modStr, startToken.Span);
        if (_parsingModuleMembers)
            modifiers |= MethodModifiers.Static;

        // --- Compact accessor shorthand ---
        // Detect accessor shorthand in accessorStr position
        if (IsAccessorShorthand(accessorStr))
        {
            var (getter, setter, initer) = ParseAccessorShorthand(accessorStr, startToken.Span, visibility);

            // v0.17 S1 / #1136 — position 9: the property's row, in the header
            // group and on the same line, exactly as a §FLD's is. Read BEFORE the
            // default-value branch for the same reason the field parser does:
            // otherwise the §E is consumed as an expression.
            var compactRow = TryParseSameLineRow(EffectRowPosition.Property);

            // Parse optional default value
            ExpressionNode? defaultValue = null;
            if (Check(TokenKind.Equals))
            {
                Advance();
                defaultValue = ParseExpression();
            }
            else if (IsExpressionStart())
            {
                defaultValue = ParseExpression();
            }

            // Compact form: no §/PROP closing tag needed
            var compactSpan = defaultValue != null ? startToken.Span.Union(defaultValue.Span) : startToken.Span;
            return WithDocumentation(new PropertyNode(
                compactSpan,
                id,
                name,
                typeName,
                visibility,
                modifiers,
                getter,
                setter,
                initer,
                defaultValue,
                attrs,
                csharpAttrs,
                GetIdentifierSpan(attrs, propertyNameKey, name),
                attrs.GetSpan(propertyTypeKey),
                compactRow), startToken);
        }

        // v0.17 S1 / #1136 — position 9 on the block form too, read before the
        // body so the §E is not taken for an accessor.
        var blockRow = TryParseSameLineRow(EffectRowPosition.Property);

        PropertyAccessorNode? getter2 = null;
        PropertyAccessorNode? setter2 = null;
        PropertyAccessorNode? initer2 = null;
        ExpressionNode? defaultValue2 = null;

        var bodyHadContent = false;
        while (!IsAtEnd && !IsMemberBlockEnd(TokenKind.EndProperty) && !IsModuleFunctionSibling(startToken))
        {
            if (Check(TokenKind.Get))
            {
                getter2 = ParsePropertyAccessor(PropertyAccessorNode.AccessorKind.Get);
                bodyHadContent = true;
            }
            else if (Check(TokenKind.Assign))
            {
                setter2 = ParsePropertyAccessor(PropertyAccessorNode.AccessorKind.Set);
                bodyHadContent = true;
            }
            else if (Check(TokenKind.Init))
            {
                initer2 = ParsePropertyAccessor(PropertyAccessorNode.AccessorKind.Init);
                bodyHadContent = true;
            }
            else if (Check(TokenKind.Equals))
            {
                // Default value prefixed with =
                Advance(); // consume =
                defaultValue2 = ParseExpression();
            }
            else if (IsExpressionStart())
            {
                // Default value without = prefix
                defaultValue2 = ParseExpression();
            }
            else
            {
                break;
            }
        }

        Token endToken;
        if (TryExpectMemberBlockEnd(TokenKind.EndProperty, startToken, bodyHadContent, out endToken))
        {
            var endAttrs = ParseAttributes();

        }

        var span = startToken.Span.Union(endToken.Span);
        return WithDocumentation(new PropertyNode(
            span,
            id,
            name,
            typeName,
            visibility,
            modifiers,
            getter2,
            setter2,
            initer2,
            defaultValue2,
            attrs,
            csharpAttrs,
            GetIdentifierSpan(attrs, propertyNameKey, name),
            attrs.GetSpan(propertyTypeKey),
            blockRow), startToken);
    }

    private IndexerNode ParseCanonicalIndexer()
    {
        var startToken = Expect(TokenKind.This);
        var parameters = new List<ParameterNode>();
        OutputNode? output = null;
        TryParseInlineSignature(startToken.Span, parameters, ref output, brackets: true);
        if (parameters.Count == 0)
            _diagnostics.ReportError(startToken.Span, DiagnosticCode.UnexpectedToken,
                "An indexer requires at least one parameter.");
        if (output == null)
            _diagnostics.ReportError(startToken.Span, DiagnosticCode.UnexpectedToken,
                "An indexer requires a return type after '→'.");
        if (output?.Row != null)
            _diagnostics.ReportError(output.Span, DiagnosticCode.UnexpectedToken,
                "Indexer return effect rows are not supported.");
        var attrs = new AttributeCollection();
        ConsumeCanonicalDeclarationModifiers(startToken, attrs);
        var visibility = ParseVisibility(attrs["_pos1"] ?? "public", startToken.Span);
        var modifiers = ParseMethodModifiers(attrs["_pos2"] ?? "", startToken.Span);
        var csharpAttrs = ParseCSharpAttributes();
        PropertyAccessorNode? getter = null;
        PropertyAccessorNode? setter = null;
        PropertyAccessorNode? initer = null;
        var bodyHadContent = false;
        while (!IsAtEnd && !IsMemberBlockEnd(TokenKind.EndIndexer) && !IsModuleFunctionSibling(startToken))
        {
            if (Check(TokenKind.Get))
            {
                if (getter != null)
                    _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken, "Duplicate indexer getter.");
                getter = ParsePropertyAccessor(PropertyAccessorNode.AccessorKind.Get);
            }
            else if (Check(TokenKind.Assign))
            {
                if (setter != null)
                    _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken, "Duplicate indexer setter.");
                setter = ParsePropertyAccessor(PropertyAccessorNode.AccessorKind.Set);
            }
            else if (Check(TokenKind.Init))
            {
                if (initer != null)
                    _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken, "Duplicate indexer initializer.");
                initer = ParsePropertyAccessor(PropertyAccessorNode.AccessorKind.Init);
            }
            else break;
            bodyHadContent = true;
        }
        TryExpectMemberBlockEnd(TokenKind.EndIndexer, startToken, bodyHadContent, out var endToken);
        if (!bodyHadContent)
            _diagnostics.ReportError(startToken.Span, DiagnosticCode.UnexpectedToken,
                "An indexer requires an accessor.");
        if (setter != null && initer != null)
            _diagnostics.ReportError(startToken.Span, DiagnosticCode.UnexpectedToken,
                "An indexer cannot declare both set and init accessors.");
        return WithDocumentation(new IndexerNode(startToken.Span.Union(endToken.Span),
            GenerateParserAutoId("ix"), output?.TypeName ?? "object", visibility, modifiers,
            parameters, getter, setter, initer, attrs, csharpAttrs), startToken);
    }

    /// <summary>
    /// Parses a property accessor.
    /// §GET
    /// §SET[pri]
    /// </summary>
    private PropertyAccessorNode ParsePropertyAccessor(PropertyAccessorNode.AccessorKind kind)
    {
        Token startToken;
        if (kind == PropertyAccessorNode.AccessorKind.Get)
        {
            startToken = Expect(TokenKind.Get);
        }
        else if (kind == PropertyAccessorNode.AccessorKind.Set)
        {
            startToken = Check(TokenKind.Assign) ? Advance() : Expect(TokenKind.Set);
        }
        else
        {
            startToken = Expect(TokenKind.Init);
        }

        var attrs = new AttributeCollection();
        if (Current.Span.Line == startToken.Span.Line && Check(TokenKind.Identifier))
        {
            var visibilityToken = Advance();
            var visibilityWord = visibilityToken.Text;
            var visibilitySpan = visibilityToken.Span;
            // A two-word accessibility (`set protected internal`, `get private protected`)
            // is one visibility, as on the declaration line.
            if (Current.Span.Line == startToken.Span.Line && Check(TokenKind.Identifier)
                && ((visibilityWord == "protected" && Current.Text == "internal")
                    || (visibilityWord == "private" && Current.Text == "protected")))
            {
                var second = Advance();
                visibilityWord += "-" + second.Text;
                visibilitySpan = visibilitySpan.Union(second.Span);
            }
            attrs.Add("_pos0", visibilityWord, visibilitySpan);
        }
        var visStr = attrs["_pos0"];
        var visibility = visStr != null ? ParseVisibility(visStr, startToken.Span) : (Visibility?)null;

        var preconditions = new List<RequiresNode>();
        var body = new List<StatementNode>();
        EffectsNode? accessorEffects = null;

        // Determine the end token for this accessor
        var endTokenKind = kind switch
        {
            PropertyAccessorNode.AccessorKind.Get => TokenKind.EndGet,
            PropertyAccessorNode.AccessorKind.Set => TokenKind.EndSet,
            PropertyAccessorNode.AccessorKind.Init => TokenKind.EndInit,
            _ => TokenKind.EndProperty
        };

        // Parse optional preconditions and body (for non-auto properties/indexers)
        // Also stop at Equals for property default values, next accessor, or end tokens.
        // #1485: the accessor owns only the lines indented under it.
        var accessorOwner = BeginBlockBody(startToken, endTokenKind, null,
            TokenKind.Get, TokenKind.Set, TokenKind.Assign, TokenKind.Init);
        while (!IsAtEnd && !Check(TokenKind.Get) &&
               !(Check(TokenKind.Assign) && Current.Span.Column <= startToken.Span.Column) &&
               !Check(TokenKind.Init) && !IsBlockEnd(TokenKind.EndProperty) && !IsBlockEnd(TokenKind.EndIndexer) &&
               !Check(TokenKind.Equals) && !IsBlockEnd(TokenKind.EndGet) && !IsBlockEnd(TokenKind.EndSet) &&
               !IsBlockEnd(TokenKind.EndInit) && IsInBlockBody(accessorOwner))
        {
            if (Check(TokenKind.Requires))
            {
                preconditions.Add(ParseRequires());
            }
            else if (Check(TokenKind.Exclamation)
                     && body.Count == 0 && preconditions.Count == 0)
            {
                // 0.30 (D1): an accessor's own row is a header clause on its own line,
                // like a constructor's. It admits the declared effects beyond the
                // intrinsic accessor contract; readers are charged the row.
                var rowToken = Current;
                var row = ParseEffects();
                if (accessorEffects != null)
                    _diagnostics.ReportError(rowToken.Span, DiagnosticCode.EffectRowMisplaced,
                        "An accessor carries one effect row. Put every permission in that row.");
                else
                    accessorEffects = row;
            }
            else
            {
                var stmt = ParseStatement();
                if (stmt != null)
                {
                    body.Add(stmt);
                }
            }
        }

        // Consume the closing token if present (§/GET or §/SET), or the
        // accessor body's own Dedent -- never the enclosing member's.
        if (Check(endTokenKind) || IsOwnedDedent(accessorOwner))
        {
            ExpectBlockEnd(endTokenKind);
        }

        if (accessorEffects != null && body.Count == 0)
            _diagnostics.ReportError(accessorEffects.Span, DiagnosticCode.EffectRowMisplaced,
                "An auto-implemented accessor has no body and cannot carry an effect row.");
        return new PropertyAccessorNode(startToken.Span, kind, visibility, preconditions, body, attrs)
        {
            Effects = body.Count == 0 ? null : accessorEffects
        };
    }

    /// <summary>
    /// Parses a constructor.
    /// §CTOR[ctor1:pub]
    ///   §I[string:name]
    ///   §Q §OP[kind=gt] §REF[name=radius] 0
    ///   §BASE §A §REF[name=name] §/BASE
    ///   §ASSIGN §REF[name=Radius] §REF[name=radius]
    /// §/CTOR[ctor1]
    /// </summary>
    private ConstructorNode ParseConstructor()
    {
        var startToken = Expect(TokenKind.Constructor);
        var attrs = new AttributeCollection();
        var temporary = new AttributeCollection();
        ConsumeCanonicalDeclarationModifiers(startToken, temporary);
        var modifiers = temporary["_pos2"];
        if (modifiers == "stat")
            _diagnostics.ReportError(startToken.Span, DiagnosticCode.InvalidModifier,
                "A static constructor is 'ctor static'.");
        else if ((modifiers != null && modifiers != "static")
            || (modifiers == "static" && temporary["_pos1"] != null))
            _diagnostics.ReportError(startToken.Span, DiagnosticCode.InvalidModifier,
                "A constructor accepts a visibility or the static modifier, not both.");
        attrs.Add("_pos0", modifiers is "static" or "stat" ? "static" : temporary["_pos1"] ?? "public");
        ValidateIdentityFreeHeader(startToken, attrs, 1);
        var csharpAttrs = ParseCSharpAttributes();

        // Positional: [id:visibility?]
        var id = GenerateParserAutoId("ctor");
        var visStr = attrs["_pos0"] ?? "public";


        var isStatic = visStr == "static";
        var visibility = isStatic ? Visibility.Private : ParseVisibility(visStr, startToken.Span);

        // --- Compact syntax: inline signature (consume but parameters already parsed via §I) ---
        var _inlineParams = new List<ParameterNode>();
        OutputNode? _inlineOutput = null;
        TryParseInlineSignature(startToken.Span, _inlineParams, ref _inlineOutput);
        if (_inlineOutput != null)
            _diagnostics.ReportError(startToken.Span, DiagnosticCode.UnexpectedToken,
                "A constructor has no return-type arrow.");

        var parameters = new List<ParameterNode>();
        // If inline signature produced parameters, use them
        if (_inlineParams.Count > 0)
        {
            parameters.AddRange(_inlineParams);
        }
        var preconditions = new List<RequiresNode>();
        ConstructorInitializerNode? initializer = null;
        EffectsNode? constructorEffects = null;
        var body = new List<StatementNode>();

        var bodyHadContent = false;
        while (!IsAtEnd && !IsMemberBlockEnd(TokenKind.EndConstructor) && !IsModuleFunctionSibling(startToken))
        {
            if (Check(TokenKind.In))
            {
                parameters.Add(ParseParameter());
                bodyHadContent = true;
            }
            else if (Check(TokenKind.Exclamation))
            {
                constructorEffects = ParseEffects();
                bodyHadContent = true;
            }
            else if (Check(TokenKind.Requires))
            {
                preconditions.Add(ParseRequires());
                bodyHadContent = true;
            }
            else if (Check(TokenKind.Base)
                && (Lookahead.Kind == TokenKind.OpenParen))
            {
                initializer = ParseConstructorInitializer(isBase: true);
                bodyHadContent = true;
            }
            else if (Check(TokenKind.This)
                && (Lookahead.Kind == TokenKind.OpenParen))
            {
                initializer = ParseConstructorInitializer(isBase: false);
                bodyHadContent = true;
            }
            else
            {
                var stmt = ParseStatement();
                if (stmt != null)
                {
                    body.Add(stmt);
                    bodyHadContent = true;
                }
            }
        }

        if (TryExpectMemberBlockEnd(TokenKind.EndConstructor, startToken, bodyHadContent, out var endToken))
        {
            var endAttrs = ParseAttributes();

        }

        var span = startToken.Span.Union(endToken.Span);
        return WithDocumentation(new ConstructorNode(span, id, visibility, parameters, preconditions, initializer, body, attrs, csharpAttrs, isStatic)
        {
            Effects = constructorEffects
        }, startToken);
    }

    /// <summary>
    /// Parses an operator overload definition.
    /// §OP{id:operator:visibility}
    ///   §I{Type:param}...
    ///   §O{ReturnType}
    ///   §Q precondition
    ///   §S postcondition
    ///   body
    /// §/OP{id}
    /// </summary>
    private OperatorOverloadNode? ParseOperatorOverload()
    {
        var startToken = Expect(TokenKind.OperatorOverload);
        var id = GenerateParserAutoId("op");
        var operatorToken = ParseOperatorToken();

        var visStr = "public";

        var csharpAttrs = ParseCSharpAttributes();
        var visibility = ParseVisibility(visStr, startToken.Span);

        // --- Compact syntax: inline signature ---
        var parameters = new List<ParameterNode>();
        var preconditions = new List<RequiresNode>();
        var postconditions = new List<EnsuresNode>();
        OutputNode? output = null;
        TryParseInlineSignature(startToken.Span, parameters, ref output);
        if (output == null)
            _diagnostics.ReportError(startToken.Span, DiagnosticCode.UnexpectedToken,
                "An operator requires a return type after '→'.");
        EffectsNode? effects = null;
        var body = new List<StatementNode>();

        var bodyHadContent = false;
        while (!IsAtEnd && !IsMemberBlockEnd(TokenKind.EndOperatorOverload) && !IsModuleFunctionSibling(startToken))
        {
            if (Check(TokenKind.In))
            {
                parameters.Add(ParseParameter());
                bodyHadContent = true;
            }
            else if (Check(TokenKind.Out))
            {
                output = ParseOutput();
                bodyHadContent = true;
            }
            else if (Check(TokenKind.Exclamation))
            {
                if (body.Count != 0)
                    _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken,
                        "Effect clauses must precede the first operator body statement.");
                if (effects != null)
                    _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken,
                        "An operator may declare only one effect row.");
                effects = ParseEffects();
                bodyHadContent = true;
            }
            else if (Check(TokenKind.Requires))
            {
                preconditions.Add(ParseRequires());
                bodyHadContent = true;
            }
            else if (Check(TokenKind.FatArrow))
            {
                if (body.Count != 0)
                    _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken,
                        "Postcondition clauses must precede the first operator body statement.");
                postconditions.Add(ParseEnsures());
                bodyHadContent = true;
            }
            else
            {
                var stmt = ParseStatement();
                if (stmt != null)
                {
                    body.Add(stmt);
                    bodyHadContent = true;
                }
            }
        }

        if (TryExpectMemberBlockEnd(TokenKind.EndOperatorOverload, startToken, bodyHadContent, out var endToken))
        {
            var endAttrs = ParseAttributes();

        }

        if (operatorToken == null)
            return null;

        var kind = OperatorOverloadNode.ResolveOperatorKind(operatorToken, parameters.Count);

        var span = startToken.Span.Union(endToken.Span);
        return WithDocumentation(new OperatorOverloadNode(span, id, operatorToken, kind, visibility, parameters, output,
            preconditions, postconditions, body, new AttributeCollection(), csharpAttrs) { Effects = effects }, startToken);
    }

    /// <summary>
    /// Parses an operator token in the context of §OP{id:operator:vis}.
    /// Handles multi-character operators like ++, --, ==, !=, etc.
    /// </summary>
    private string? ParseOperatorToken()
    {
        var token = Current;
        switch (token.Kind)
        {
            case TokenKind.Plus:
                Advance();
                if (Check(TokenKind.Plus)) { Advance(); return "++"; }
                return "+";
            case TokenKind.Minus:
                Advance();
                if (Check(TokenKind.Minus)) { Advance(); return "--"; }
                return "-";
            case TokenKind.Star:
                Advance();
                return "*";
            case TokenKind.Slash:
                Advance();
                return "/";
            case TokenKind.Percent:
                Advance();
                return "%";
            case TokenKind.EqualEqual:
                Advance();
                return "==";
            case TokenKind.BangEqual:
                Advance();
                return "!=";
            case TokenKind.Less:
                Advance();
                return "<";
            case TokenKind.Greater:
                Advance();
                return ">";
            case TokenKind.LessEqual:
                Advance();
                return "<=";
            case TokenKind.GreaterEqual:
                Advance();
                return ">=";
            case TokenKind.Amp:
                Advance();
                return "&";
            case TokenKind.Pipe:
                Advance();
                return "|";
            case TokenKind.Caret:
                Advance();
                return "^";
            case TokenKind.LessLess:
                Advance();
                return "<<";
            case TokenKind.GreaterGreater:
                Advance();
                return ">>";
            case TokenKind.Exclamation:
                Advance();
                return "!";
            case TokenKind.Tilde:
                Advance();
                return "~";
            case TokenKind.BoolLiteral:
                var text = token.Text;
                Advance();
                return text;
            case TokenKind.Identifier when token.Text is "implicit" or "explicit":
                var convText = token.Text;
                Advance();
                return convText;
            default:
                _diagnostics.ReportError(token.Span, DiagnosticCode.UnexpectedToken,
                    $"Expected an operator token after 'op', found '{token.Text}'");
                Advance();
                return null;
        }
    }

    /// <summary>
    /// Parses a constructor initializer (: base(...) or : this(...)).
    /// </summary>
    private ConstructorInitializerNode ParseConstructorInitializer(bool isBase)
    {
        var startToken = isBase ? Expect(TokenKind.Base) : Expect(TokenKind.This);

        var arguments = new List<ExpressionNode>();
        Expect(TokenKind.OpenParen);
        if (!Check(TokenKind.CloseParen))
        {
            do arguments.Add(ParseExpression()); while (Match(TokenKind.Comma));
        }
        Expect(TokenKind.CloseParen);
        return new ConstructorInitializerNode(startToken.Span.Union(PreviousToken.Span), isBase, arguments);
    }

    /// <summary>
    /// Parses an assignment statement.
    /// §ASSIGN §REF[name=Radius] §REF[name=radius]
    /// </summary>
    private AssignmentStatementNode ParseAssignmentStatement()
    {
        var startToken = Expect(TokenKind.Assign);

        // The whitespace-separated value may start with unary minus.
        var target = ParsePositionalExpression();
        var value = ParseExpression();

        var span = startToken.Span.Union(value.Span);
        return new AssignmentStatementNode(span, target, value);
    }

    // Phase 10: Try/Catch/Finally

    /// <summary>
    /// Parses a try/catch/finally statement.
    /// §TRY[try1]
    ///   ...
    /// §CATCH[IOException:ex]
    ///   ...
    /// §FINALLY
    ///   ...
    /// §/TRY[try1]
    /// </summary>
    /// <summary>
    /// Parses a using statement.
    /// §USE{id:variableName:variableType} resource-expression
    ///   ...body...
    /// §/USE{id}
    /// </summary>
    /// <summary>
    /// True when the tokens ahead are a typed binder <c>type:name</c> followed by one of
    /// <paramref name="followers"/>. The type may be dotted, generic (<c>IEnumerator&lt;int&gt;</c>,
    /// nested closers lexed as <c>&gt;&gt;</c>), an array or nullable.
    /// </summary>
    private bool IsTypedBinderAhead(params TokenKind[] followers)
    {
        if (!Check(TokenKind.Identifier))
            return false;
        var depth = 0;
        for (var i = 1; i < 64; i++)
        {
            switch (Peek(i).Kind)
            {
                case TokenKind.Less:
                    depth++;
                    break;
                case TokenKind.Greater:
                    depth--;
                    break;
                case TokenKind.GreaterGreater:
                    depth -= 2;
                    break;
                case TokenKind.Colon when depth == 0:
                    return Peek(i + 1).Kind == TokenKind.Identifier
                        && Array.IndexOf(followers, Peek(i + 2).Kind) >= 0;
                case TokenKind.Identifier or TokenKind.Comma or TokenKind.OpenBracket or TokenKind.CloseBracket
                    or TokenKind.Question or TokenKind.NullCoalesce:
                    break;
                default:
                    return false;
            }
            if (depth < 0)
                return false;
        }
        return false;
    }

    private UsingStatementNode ParseUsingStatement()
    {
        var startToken = Expect(Check(TokenKind.Using) ? TokenKind.Using : TokenKind.Use);
        RejectLegacyLoopHeader(startToken, "using [type:]name = resource or using resource");
        var id = GenerateParserAutoId("use");
        string? variableName = null;
        string? variableType = null;
        TextSpan? variableSpan = null;
        if (Check(TokenKind.Identifier) && Peek(1).Kind == TokenKind.Equals)
        {
            var binder = Advance();
            variableName = binder.Text;
            variableSpan = binder.Span;
            Advance();
        }
        else if (IsTypedBinderAhead(TokenKind.Equals))
        {
            variableType = ReadInlineTypeToken();
            Expect(TokenKind.Colon);
            var binder = Expect(TokenKind.Identifier);
            variableName = binder.Text;
            variableSpan = binder.Span;
            Expect(TokenKind.Equals);
        }
        var resource = ParseExpression(allowSpacedCall: false);

        // Parse body statements
        var bodyOwner = BeginBlockBody(startToken, TokenKind.EndUse, id);
        var body = ParseOwnedStatementBlock(bodyOwner, TokenKind.EndUse);

        var endToken = ExpectOwnedBlockEnd(TokenKind.EndUse, bodyOwner);
        var span = startToken.Span.Union(endToken.Span);
        return new UsingStatementNode(
            span,
            id,
            variableName,
            variableType,
            resource,
            body,
            variableSpan);
    }

    private TryStatementNode ParseTryStatement()
    {
        var startToken = Expect(TokenKind.Try);
        RejectLegacyLoopHeader(startToken, "try with catch and/or finally clauses");
        var attrs = new AttributeCollection();

        // Positional: [id]
        var id = GenerateParserAutoId("tr");


        // Parse try body. #1485: each clause (§TR/§CA/§FI) owns only the lines
        // indented under it; an empty §CA/§FI leaves a same-column sibling and
        // the enclosing block's Dedent to the enclosing block.
        var clauseOwner = BeginBlockBody(startToken, TokenKind.EndTry, id);
        var tryBody = new List<StatementNode>();
        while (!IsAtEnd && !Check(TokenKind.Catch) && !Check(TokenKind.Finally) && !IsBlockEnd(TokenKind.EndTry)
               && IsInBlockBody(clauseOwner))
        {
            var stmt = ParseStatement();
            if (stmt != null)
            {
                tryBody.Add(stmt);
            }
        }

        // Phase 3 (indent-aware): consume the try body's own Dedent before §CA/§FI.
        ConsumeOwnedDedentBeforeChain(clauseOwner, TokenKind.Catch, TokenKind.Finally);

        // Parse catch clauses
        var chainOwner = clauseOwner;
        var catchClauses = new List<CatchClauseNode>();
        while (Check(TokenKind.Catch) && ContinuesChain(chainOwner))
        {
            catchClauses.Add(ParseCatchClause(id, out clauseOwner));
        }

        // Parse optional finally
        List<StatementNode>? finallyBody = null;
        if (Check(TokenKind.Finally) && ContinuesChain(chainOwner))
        {
            var finallyToken = Expect(TokenKind.Finally);
            finallyBody = new List<StatementNode>();
            clauseOwner = BeginBlockBody(finallyToken, TokenKind.EndTry, id);
            while (!IsAtEnd && !IsBlockEnd(TokenKind.EndTry) && IsInBlockBody(clauseOwner))
            {
                var stmt = ParseStatement();
                if (stmt != null)
                {
                    finallyBody.Add(stmt);
                }
            }
        }

        var endToken = ExpectOwnedBlockEnd(TokenKind.EndTry, clauseOwner);
        if (catchClauses.Count == 0 && finallyBody == null)
            _diagnostics.ReportError(startToken.Span, DiagnosticCode.UnexpectedToken,
                "A try statement requires a catch or finally clause.");
        var span = startToken.Span.Union(endToken.Span);
        return new TryStatementNode(span, id, tryBody, catchClauses, finallyBody, attrs);
    }

    /// <summary>
    /// Parses a catch clause.
    /// §CATCH[IOException:ex]
    /// §CATCH
    /// </summary>
    private CatchClauseNode ParseCatchClause(string tryId, out BlockOwner clauseOwner)
    {
        var startToken = Expect(TokenKind.Catch);
        RejectLegacyLoopHeader(startToken, "catch [ExceptionType[:name]] [when condition]");
        var attrs = new AttributeCollection();
        string? exceptionType = null;
        string? variableName = null;
        TextSpan? exceptionSpan = null;
        TextSpan? variableSpan = null;
        if (Current.Span.Line == startToken.Span.Line && Check(TokenKind.Identifier))
        {
            exceptionSpan = Current.Span;
            exceptionType = ParseValue();
            if (Match(TokenKind.Colon))
            {
                var variable = Expect(TokenKind.Identifier);
                variableName = variable.Text;
                variableSpan = variable.Span;
            }
        }

        // Catch filters accept the exact-case WHEN keyword.
        ExpressionNode? filter = null;
        if (Check(TokenKind.When))
        {
            Expect(TokenKind.When);
            filter = ParseExpression();
        }

        // Parse catch body (#1485: only lines indented under the §CA).
        clauseOwner = BeginBlockBody(startToken, TokenKind.EndTry, tryId);
        var body = new List<StatementNode>();
        while (!IsAtEnd && !Check(TokenKind.Catch) && !Check(TokenKind.Finally) && !IsBlockEnd(TokenKind.EndTry)
               && IsInBlockBody(clauseOwner))
        {
            var stmt = ParseStatement();
            if (stmt != null)
            {
                body.Add(stmt);
            }
        }

        // Phase 3 (indent-aware): consume this clause's own Dedent before the next §CA/§FI.
        ConsumeOwnedDedentBeforeChain(clauseOwner, TokenKind.Catch, TokenKind.Finally);

        var span = body.Count > 0 ? startToken.Span.Union(body[^1].Span) : startToken.Span;
        return new CatchClauseNode(
            span,
            exceptionType,
            variableName,
            filter,
            body,
            attrs,
            variableSpan,
            exceptionSpan);
    }

    private ThrowExpressionNode ParseThrowExpression()
    {
        var startToken = Expect(TokenKind.Throw);
        var exception = ParseExpression();
        return new ThrowExpressionNode(startToken.Span.Union(exception.Span), exception);
    }

    /// <summary>
    /// Parses a throw statement.
    /// §THROW §NEW[ArgumentException] §A "Invalid" §/NEW
    /// </summary>
    private ThrowStatementNode ParseThrowStatement()
    {
        var startToken = Expect(TokenKind.Throw);

        ExpressionNode? exception = null;
        if (Current.Span.Line == startToken.Span.Line && IsExpressionStart())
        {
            exception = ParseExpression();
        }

        var span = exception != null ? startToken.Span.Union(exception.Span) : startToken.Span;
        return new ThrowStatementNode(span, exception);
    }

    /// <summary>
    /// Parses a break statement.
    /// §BREAK
    /// </summary>
    private BreakStatementNode ParseBreakStatement()
    {
        var token = Expect(TokenKind.Break);
        return new BreakStatementNode(token.Span);
    }

    /// <summary>
    /// Parses a continue statement.
    /// §CONTINUE
    /// </summary>
    private ContinueStatementNode ParseContinueStatement()
    {
        var token = Expect(TokenKind.Continue);
        return new ContinueStatementNode(token.Span);
    }

    /// <summary>
    /// Parses a label jump or a switch case/default jump.
    /// </summary>
    private GotoStatementNode ParseGotoStatement()
    {
        var token = Expect(TokenKind.Goto);
        if (Current.Span.Line != token.Span.Line)
        {
            _diagnostics.ReportError(token.Span, DiagnosticCode.ExpectedExpression,
                "A goto requires a same-line label, 'case value', or 'default'.");
            return new GotoStatementNode(token.Span, "");
        }
        if (Match(TokenKind.Case))
        {
            if (Current.Span.Line != token.Span.Line)
            {
                _diagnostics.ReportError(token.Span, DiagnosticCode.ExpectedExpression,
                    "A goto case requires a same-line constant expression.");
                return new GotoStatementNode(token.Span, "") { CaseLabel = new IntLiteralNode(token.Span, 0) };
            }
            var value = ParseExpression(allowSpacedCall: false);
            return new GotoStatementNode(token.Span.Union(value.Span), "") { CaseLabel = value };
        }
        if (Current.Text == "default")
            return new GotoStatementNode(token.Span.Union(Advance().Span), "") { IsDefault = true };
        var label = Expect(TokenKind.Identifier);
        return new GotoStatementNode(token.Span.Union(label.Span), TokenIdentifier(label));
    }

    /// <summary>
    /// Parses a name followed by a colon.
    /// </summary>
    private LabelStatementNode ParseLabelStatement()
    {
        var token = Expect(TokenKind.Identifier);
        var colon = Expect(TokenKind.Colon);
        return new LabelStatementNode(token.Span.Union(colon.Span), TokenIdentifier(token));
    }

    /// <summary>
    /// Parses a raw C# passthrough block.
    /// §RAW ... §/RAW (content already captured by lexer as single token)
    /// </summary>
    private RawCSharpNode ParseRawCSharpStatement()
    {
        var token = Expect(Check(TokenKind.CSharpInterop)
            ? TokenKind.CSharpInterop : TokenKind.RawCSharp);
        var content = token.Value is RawCSharpFrame frame
            ? frame.Code : token.Value as string ?? "";
        var reason = token.Kind == TokenKind.CSharpInterop && token.Value is RawCSharpFrame metadata
            ? metadata.ReasonMarker : null;
        return new RawCSharpNode(token.Span, content, reason);
    }

    private static bool IsCompilerDirectivePrefix(AstNode item) => item switch
    {
        CompilerDirectiveNode => true,
        TypePreprocessorBlockNode block => block.Items.All(IsCompilerDirectivePrefix)
            && (block.ElseBranch == null || IsCompilerDirectivePrefix(block.ElseBranch)),
        _ => false
    };

    private CompilerDirectiveNode ParseCompilerDirective(bool allowSymbolDirective = false)
    {
        var token = Expect(TokenKind.CompilerDirective);
        if (token.Value is not RawCSharpFrame { IsCompilerDirective: true, FeatureName: { } feature } frame)
        {
            _diagnostics.ReportError(token.Span, DiagnosticCode.InvalidRawFrame,
                "A compiler directive requires a validated raw directive frame.");
            return new CompilerDirectiveNode(token.Span, "", "compiler-directive");
        }
        if (!frame.TryValidateCompilerDirective(out var error))
            _diagnostics.ReportError(token.Span, DiagnosticCode.InvalidRawFrame, error);
        if (frame.FeatureName == "symbol-directive" && !allowSymbolDirective)
            _diagnostics.ReportError(token.Span, DiagnosticCode.InvalidRawFrame,
                "#define and #undef are allowed only before C# declarations or using directives at compilation-unit scope.");
        return new CompilerDirectiveNode(token.Span, frame.Code, feature);
    }

    /// <summary>
    /// Parses a preprocessor conditional block.
    /// Native pp condition / pp elif condition / pp else at the owner's indentation.
    /// </summary>
    private PreprocessorDirectiveNode ParsePreprocessorDirective(bool continuation = false)
    {
        var startToken = Expect(TokenKind.Preprocessor);
        var condition = ParseNativePreprocessorCondition(startToken, continuation);
        var body = new List<StatementNode>();
        List<StatementNode>? elseBody = null;
        var ppOwner = BeginBlockBody(startToken, TokenKind.EndPreprocessor, null);
        while (!IsBlockEnd(TokenKind.EndPreprocessor) && !Check(TokenKind.Eof)
               && IsInBlockBody(ppOwner))
        {
            var stmt = ParseStatement();
            if (stmt != null) body.Add(stmt);
        }

        ConsumeOwnedDedentBeforeChain(ppOwner, TokenKind.Preprocessor, TokenKind.EndPreprocessor);
        var endSpan = body.LastOrDefault()?.Span ?? startToken.Span;
        if (condition.Length != 0 && IsNativePreprocessorContinuation(startToken))
        {
            var branch = ParsePreprocessorDirective(continuation: true);
            elseBody = branch.Condition.Length == 0 ? branch.Body.ToList() : [branch];
            endSpan = branch.Span;
        }
        else
            ExpectOwnedBlockEnd(TokenKind.EndPreprocessor, ppOwner);
        return new PreprocessorDirectiveNode(startToken.Span.Union(endSpan), condition, body, elseBody);
    }

    /// <summary>
    /// Parses a member-level preprocessor block inside a class body.
    /// §PP{CONDITION} ... members ... §PPE ... §/PP{CONDITION}
    /// </summary>
    private MemberPreprocessorBlockNode ParseMemberPreprocessorBlock(bool continuation = false,
        bool interfaceMembers = false)
    {
        var startToken = Expect(TokenKind.Preprocessor);
        var condition = ParseNativePreprocessorCondition(startToken, continuation);

        var fields = new List<ClassFieldNode>();
        var properties = new List<PropertyNode>();
        var indexers = new List<IndexerNode>();
        var constructors = new List<ConstructorNode>();
        var methods = new List<MethodNode>();
        var events = new List<EventDefinitionNode>();
        var operatorOverloads = new List<OperatorOverloadNode>();
        var interopBlocks = new List<CSharpInteropBlockNode>();
        var items = new List<AstNode>();

        // Parse members until §PPE or §/PP
        // #1485: an indent-form §PP owns only the lines indented under it.
        var ppOwner = BeginBlockBody(startToken, TokenKind.EndPreprocessor, null);
        while (!IsBlockEnd(TokenKind.EndPreprocessor) && !Check(TokenKind.Eof)
               && IsInBlockBody(ppOwner))
        {
            ParseMemberInPreprocessorBlock(
                fields, properties, constructors, methods, events,
                operatorOverloads, indexers: indexers, interopBlocks: interopBlocks, items: items,
                interfaceMembers: interfaceMembers);
        }
        ConsumeOwnedDedentBeforeChain(
            ppOwner,
            TokenKind.Preprocessor,
            TokenKind.PreprocessorElse,
            TokenKind.EndPreprocessor);

        MemberPreprocessorBlockNode? elseBranch = null;
        if (condition.Length != 0 && IsNativePreprocessorContinuation(startToken))
            elseBranch = ParseMemberPreprocessorBlock(continuation: true, interfaceMembers: interfaceMembers);
        else
            ExpectOwnedBlockEnd(TokenKind.EndPreprocessor, ppOwner);
        return new MemberPreprocessorBlockNode(startToken.Span, condition,
            fields, properties, constructors, methods, events, operatorOverloads,
            elseBranch, indexers: indexers, interopBlocks: interopBlocks, items: items);
    }

    /// <summary>
    /// Parses a type-level preprocessor block at module level.
    /// §PP{CONDITION} ... type declarations ... §PPE ... §/PP{CONDITION}
    /// </summary>
    private TypePreprocessorBlockNode ParseTypePreprocessorBlock(
        bool continuation = false, bool allowSymbolDirectives = false)
    {
        var startToken = Expect(TokenKind.Preprocessor);
        var condition = ParseNativePreprocessorCondition(startToken, continuation);

        var usings = new List<UsingDirectiveNode>();
        var classes = new List<ClassDefinitionNode>();
        var interfaces = new List<InterfaceDefinitionNode>();
        var enums = new List<EnumDefinitionNode>();
        var delegates = new List<DelegateDefinitionNode>();
        var nestedBlocks = new List<TypePreprocessorBlockNode>();
        var interopBlocks = new List<CSharpInteropBlockNode>();
        var items = new List<AstNode>();

        // Parse type declarations until §PPE or §/PP
        // #1485: an indent-form §PP owns only the lines indented under it.
        var ppOwner = BeginBlockBody(startToken, TokenKind.EndPreprocessor, null);
        while (!IsBlockEnd(TokenKind.EndPreprocessor) && !Check(TokenKind.Eof)
               && IsInBlockBody(ppOwner))
        {
            ParseTypeInPreprocessorBlock(
                classes,
                interfaces,
                enums,
                delegates,
                usings,
                nestedBlocks,
                items,
                interopBlocks,
                allowSymbolDirectives);
        }
        ConsumeOwnedDedentBeforeChain(
            ppOwner,
            TokenKind.Preprocessor,
            TokenKind.PreprocessorElse,
            TokenKind.EndPreprocessor);

        TypePreprocessorBlockNode? elseBranch = null;
        if (condition.Length != 0 && IsNativePreprocessorContinuation(startToken))
            elseBranch = ParseTypePreprocessorBlock(
                continuation: true, allowSymbolDirectives: allowSymbolDirectives);
        else
            ExpectOwnedBlockEnd(TokenKind.EndPreprocessor, ppOwner);
        return new TypePreprocessorBlockNode(startToken.Span, condition,
            classes, interfaces, enums, delegates, elseBranch, usings,
            nestedBlocks, items, interopBlocks);
    }

    private bool IsNativePreprocessorContinuation(Token opener) =>
        Check(TokenKind.Preprocessor) && Current.Span.Column == opener.Span.Column
        && Peek(1).Span.Line == Current.Span.Line && Peek(1).Text is "elif" or "else";

    private string ParseNativePreprocessorCondition(Token start, bool continuation)
    {
        var first = Current;
        var branch = first.Span.Line == start.Span.Line && first.Text is "elif" or "else"
            ? first.Text : null;
        if (branch != null && !continuation)
            _diagnostics.ReportError(start.Span, DiagnosticCode.UnexpectedToken,
                $"'pp {branch}' requires a preceding branch at the same indentation.");
        while (!IsAtEnd && !Check(TokenKind.Dedent) && Current.Span.Line == start.Span.Line)
            Advance();
        if (start.Value is not string source)
        {
            _diagnostics.ReportError(start.Span, DiagnosticCode.UnexpectedToken,
                "A native 'pp' header requires its source-backed physical-line condition.");
            return "";
        }
        var condition = source.Trim();
        if (branch != null)
            condition = condition[branch.Length..].TrimStart();
        if (branch == "else")
        {
            if (condition.Length != 0)
                _diagnostics.ReportError(start.Span, DiagnosticCode.UnexpectedToken,
                    "'pp else' cannot carry a condition.");
            return "";
        }
        if (condition.Length == 0 || SyntaxFactory.ParseLeadingTrivia($"#if {condition}\n#endif\n")
            .Any(trivia => trivia.ContainsDiagnostics))
            _diagnostics.ReportError(start.Span, DiagnosticCode.UnexpectedToken,
                "'pp' requires a valid same-line C# preprocessor condition.");
        return condition;
    }

    private void ParseTypeInPreprocessorBlock(
        List<ClassDefinitionNode> classes,
        List<InterfaceDefinitionNode> interfaces,
        List<EnumDefinitionNode> enums,
        List<DelegateDefinitionNode> delegates,
        List<UsingDirectiveNode>? usings = null,
        List<TypePreprocessorBlockNode>? nestedBlocks = null,
        List<AstNode>? items = null,
        List<CSharpInteropBlockNode>? interopBlocks = null,
        bool allowSymbolDirectives = false)
    {
        if (usings != null
            && (Check(TokenKind.Using) || Current.Text == "global" && Peek(1).Kind == TokenKind.Using))
        {
            var node = ParseUsingDirective();
            usings.Add(node);
            items?.Add(node);
        }
        else if (Check(TokenKind.Class) || Check(TokenKind.Record) || Check(TokenKind.Module))
        {
            var node = ParseClassDefinition();
            classes.Add(node);
            items?.Add(node);
        }
        else if (Check(TokenKind.Interface))
        {
            var node = ParseInterfaceDefinition();
            interfaces.Add(node);
            items?.Add(node);
        }
        else if (Check(TokenKind.Enum))
        {
            var node = ParseEnumDefinition();
            enums.Add(node);
            items?.Add(node);
        }
        else if (Check(TokenKind.Delegate))
        {
            var node = ParseDelegateDefinition();
            delegates.Add(node);
            items?.Add(node);
        }
        else if (nestedBlocks != null && Check(TokenKind.Preprocessor))
        {
            var node = ParseTypePreprocessorBlock(allowSymbolDirectives:
                allowSymbolDirectives && items != null && items.All(IsCompilerDirectivePrefix));
            nestedBlocks.Add(node);
            items?.Add(node);
        }
        else if (interopBlocks != null && Check(TokenKind.CSharpInterop))
        {
            var node = ParseCSharpInteropBlock();
            interopBlocks.Add(node);
            items?.Add(node);
        }
        else if (Check(TokenKind.CompilerDirective))
        {
            items?.Add(ParseCompilerDirective(
                allowSymbolDirectives && items != null && items.All(IsCompilerDirectivePrefix)));
        }
        else if (Check(TokenKind.Preprocessor))
        {
            items?.Add(ParseMemberPreprocessorBlock());
        }
        else if (Check(TokenKind.Class) || Check(TokenKind.Record))
        {
            items?.Add(ParseClassDefinition());
        }
        else if (Check(TokenKind.Interface))
        {
            items?.Add(ParseInterfaceDefinition());
        }
        else if (Check(TokenKind.Enum))
        {
            items?.Add(ParseEnumDefinition());
        }
        else if (Check(TokenKind.Delegate))
        {
            items?.Add(ParseDelegateDefinition());
        }
        else
        {
            _diagnostics.ReportUnexpectedToken(Current.Span, "U, CLASS, IFACE, EN, DEL, PP, CSHARP, PPE, or END_PP", Current.Kind);
            Advance();
        }
    }

    private void ParseMemberInPreprocessorBlock(
        List<ClassFieldNode> fields,
        List<PropertyNode> properties,
        List<ConstructorNode> constructors,
        List<MethodNode> methods,
        List<EventDefinitionNode> events,
        List<OperatorOverloadNode> operatorOverloads,
        List<IndexerNode>? indexers = null,
        List<CSharpInteropBlockNode>? interopBlocks = null,
        List<AstNode>? items = null,
        bool interfaceMembers = false)
    {
        if (Check(TokenKind.FieldDef))
        {
            var node = ParseClassField();
            fields.Add(node);
            items?.Add(node);
        }
        else if (Check(TokenKind.Property))
        {
            var node = ParseProperty();
            properties.Add(node);
            items?.Add(node);
        }
        else if (Check(TokenKind.This))
        {
            var node = ParseCanonicalIndexer();
            indexers?.Add(node);
            items?.Add(node);
        }
        else if (Check(TokenKind.Constructor))
        {
            var node = ParseConstructor();
            constructors.Add(node);
            items?.Add(node);
        }
        else if (Check(TokenKind.OperatorOverload))
        {
            var node = ParseOperatorOverload();
            if (node != null)
            {
                operatorOverloads.Add(node);
                items?.Add(node);
            }
        }
        else if (interfaceMembers && Check(TokenKind.Func))
        {
            items?.Add(ParseMethodSignature());
        }
        else if (Check(TokenKind.Func))
        {
            var node = ParseMethodDefinition();
            methods.Add(node);
            items?.Add(node);
        }
        else if (Check(TokenKind.Event))
        {
            var node = ParseEventDefinition();
            events.Add(node);
            items?.Add(node);
        }
        else if (interopBlocks != null && Check(TokenKind.CSharpInterop))
        {
            var node = ParseCSharpInteropBlock();
            interopBlocks.Add(node);
            items?.Add(node);
        }
        else if (Check(TokenKind.CompilerDirective))
        {
            items?.Add(ParseCompilerDirective());
        }
        else if (Check(TokenKind.Preprocessor))
        {
            items?.Add(ParseMemberPreprocessorBlock(interfaceMembers: interfaceMembers));
        }
        else if (Check(TokenKind.Class) || Check(TokenKind.Record) || Check(TokenKind.Module))
        {
            items?.Add(ParseClassDefinition());
        }
        else if (Check(TokenKind.Interface))
        {
            items?.Add(ParseInterfaceDefinition());
        }
        else if (Check(TokenKind.Enum))
        {
            items?.Add(ParseEnumDefinition());
        }
        else if (Check(TokenKind.Delegate))
        {
            items?.Add(ParseDelegateDefinition());
        }
        else
        {
            _diagnostics.ReportUnexpectedToken(Current.Span, "FLD, PROP, IXER, CTOR, OP, METHOD, AMT, EVT, CSHARP, PPE, or END_PP", Current.Kind);
            Advance();
        }
    }

    /// <summary>
    /// Parses yield value or yield break without crossing a statement line.
    /// </summary>
    private StatementNode ParseYieldReturnStatement()
    {
        var startToken = Expect(TokenKind.Yield);

        if (Current.Span.Line == startToken.Span.Line && Match(TokenKind.Break))
            return new YieldBreakStatementNode(startToken.Span.Union(PreviousToken.Span));
        if (Current.Span.Line != startToken.Span.Line || !IsExpressionStart())
        {
            _diagnostics.ReportError(startToken.Span, DiagnosticCode.ExpectedExpression,
                "Use 'yield value' or 'yield break' on one line.");
            return new YieldReturnStatementNode(startToken.Span, null);
        }
        var expression = ParseExpression();
        return new YieldReturnStatementNode(startToken.Span.Union(expression.Span), expression);
    }

    /// <summary>
    /// Parses a delegate definition.
    /// §DEL[d001:Processor] §I[string:input] §O[bool] §E[fr,fw] §/DEL[d001]
    /// </summary>
    private DelegateDefinitionNode ParseDelegateDefinition()
    {
        var startToken = Expect(TokenKind.Delegate);
        var attrs = ParseDeclarationAttributes(startToken);
        ValidateIdentityFreeHeader(startToken, attrs, 1);

        // Positional: [id:name]
        var id = GenerateParserAutoId("del");
        var name = attrs["_pos0"] ?? "";

        if (string.IsNullOrEmpty(name))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "DEL", "name");
        }

        var parameters = new List<ParameterNode>();
        OutputNode? output = null;
        EffectsNode? effects = null;
        if (attrs["_pos2"] != null
            || attrs["_pos1"] is { } visibility && ParseVisibility(visibility, startToken.Span) != Visibility.Public)
            _diagnostics.ReportError(startToken.Span, DiagnosticCode.InvalidModifier,
                "Native delegates currently support public, non-generic declarations only.");
        TryParseInlineSignature(startToken.Span, parameters, ref output);
        var owner = BeginBlockBody(startToken, TokenKind.EndDelegate, id);
        while (!IsAtEnd && IsInBlockBody(owner) && !Check(TokenKind.Dedent))
        {
            if (Check(TokenKind.Exclamation))
                effects = ParseEffects(EffectRowPosition.Delegate);
            else
            {
                _diagnostics.ReportUnexpectedToken(Current.Span, "a delegate effect row", Current.Kind);
                Advance();
            }
        }
        var end = ExpectOwnedBlockEnd(TokenKind.EndDelegate, owner);
        return WithDocumentation(new DelegateDefinitionNode(startToken.Span.Union(end.Span),
            id, name, parameters, output, effects, attrs, GetIdentifierSpan(attrs, "_pos0", name)), startToken);
    }

    /// <summary>
    /// Parses an event definition.
    /// §EVT[e001:Click:pub:EventHandler]
    /// </summary>
    private EventDefinitionNode ParseEventDefinition()
    {
        var startToken = Expect(TokenKind.Event);
        return ParseCanonicalEvent(startToken);
    }

    private EventDefinitionNode ParseCanonicalEvent(Token start)
    {
        var attrs = ParseTypedDeclarationAttributes(start, typeFirst: false);
        var modifiers = attrs["_pos3"]?.Split(',') ?? [];
        if (modifiers.Any(modifier => modifier != "static"))
            _diagnostics.ReportError(start.Span, DiagnosticCode.InvalidModifier,
                "Only the static modifier is represented by this event declaration.");
        var name = attrs["_pos0"] ?? "";
        var type = attrs["_pos1"] ?? "";
        var visibility = ParseVisibility(attrs["_pos2"], start.Span);
        var owner = BeginBlockBody(start, TokenKind.EndEvent, null);
        List<StatementNode>? addBody = null;
        List<StatementNode>? removeBody = null;
        while (!IsAtEnd && !Check(TokenKind.Dedent) && IsInBlockBody(owner))
        {
            if (Current.Text == "add")
            {
                if (addBody != null)
                    _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken, "Duplicate event add accessor.");
                addBody = ParseEventAccessorBody(TokenKind.EventAdd, TokenKind.EndEventAdd);
            }
            else if (Current.Text == "remove")
            {
                if (removeBody != null)
                    _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken, "Duplicate event remove accessor.");
                removeBody = ParseEventAccessorBody(TokenKind.EventRemove, TokenKind.EndEventRemove);
            }
            else
            {
                _diagnostics.ReportUnexpectedToken(Current.Span, "'add' or 'remove'", Current.Kind);
                Advance();
            }
        }
        if ((addBody == null) != (removeBody == null))
            _diagnostics.ReportError(start.Span, DiagnosticCode.UnexpectedToken,
                "An explicit event requires both add and remove accessors.");
        var end = ExpectOwnedBlockEnd(TokenKind.EndEvent, owner);
        return WithDocumentation(new EventDefinitionNode(start.Span.Union(end.Span),
            GenerateParserAutoId("ev"), name, visibility, type, attrs, addBody, removeBody,
            attrs.GetSpan("_pos1"), isStatic: _parsingModuleMembers || modifiers.Contains("static")), start);
    }

    private List<StatementNode> ParseEventAccessorBody(TokenKind startKind, TokenKind endKind)
    {
        var startToken = Advance(); // consume §EADD or §EREM

        var body = new List<StatementNode>();

        // #1485: the accessor owns only the lines indented under it.
        var accessorOwner = BeginBlockBody(startToken, endKind, null, TokenKind.EventAdd, TokenKind.EventRemove);
        while (!IsAtEnd && !Check(endKind) && !IsBlockEnd(TokenKind.EndEvent) && !Check(TokenKind.Dedent)
               && IsInBlockBody(accessorOwner))
        {
            var stmt = ParseStatement();
            if (stmt != null)
            {
                body.Add(stmt);
            }
        }

        // Consume the closing token if present (§/EADD or §/EREM), or the
        // accessor body's own Dedent -- never the enclosing event's.
        if (Check(endKind) || IsOwnedDedent(accessorOwner))
        {
            ExpectBlockEnd(endKind);
        }

        return body;
    }

    // Phase 12: Async/Await

    /// <summary>
    /// Parses native await with the same operand precedence as other unary operators.
    /// </summary>
    private AwaitExpressionNode ParseAwaitExpression()
    {
        var startToken = Expect(TokenKind.Await);
        var awaited = ParseInfixOperand(startToken, 15);

        var span = startToken.Span.Union(awaited.Span);
        return new AwaitExpressionNode(span, awaited);
    }

    private int FindQuantifierRangeDelimiter()
    {
        var depth = 0;
        for (var position = _position; position < _tokens.Count
            && _tokens[position].Span.Line == Current.Span.Line; position++)
        {
            var kind = _tokens[position].Kind;
            if (kind is TokenKind.OpenParen or TokenKind.OpenBracket or TokenKind.OpenBrace)
                depth++;
            else if (kind is TokenKind.CloseParen or TokenKind.CloseBracket or TokenKind.CloseBrace)
            {
                if (depth == 0)
                    return -1;
                depth--;
            }
            else if (depth == 0 && kind == TokenKind.RangeOp)
                return position;
        }
        return -1;
    }

    private RangeExpressionNode ParseRangeExpression()
    {
        var startToken = Expect(TokenKind.RangeOp);

        if (Check(TokenKind.RangeOp))
            _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken,
                "Range operators cannot be chained without parentheses.");
        var end = Current.Span.Line == TokenEndLine(startToken) && IsExpressionStart()
            ? ParseExpression(15) : null;
        return new RangeExpressionNode(startToken.Span.Union(end?.Span ?? startToken.Span), null, end);
    }

    /// <summary>
    /// Parses an index-from-end expression.
    /// §^ 1
    /// </summary>
    private IndexFromEndNode ParseIndexFromEnd()
    {
        var startToken = Check(TokenKind.Caret) ? Advance() : Expect(TokenKind.IndexEnd);
        var offset = ParseInfixOperand(startToken, 15);

        var span = startToken.Span.Union(offset.Span);
        return new IndexFromEndNode(span, offset);
    }

    // Phase 10: Advanced Patterns

    /// <summary>
    /// Parses a native record clone with ordered property initializers.
    /// </summary>
    private WithExpressionNode ParseWithExpression(ExpressionNode target)
    {
        Expect(TokenKind.With);
        Expect(TokenKind.OpenBrace);
        var assignments = new List<WithPropertyAssignmentNode>();
        while (!IsAtEnd && !Check(TokenKind.CloseBrace) && !Check(TokenKind.Dedent))
        {
            var escaped = Match(TokenKind.At);
            var property = escaped && Current.IsKeyword ? Advance() : Expect(TokenKind.Identifier);
            Expect(TokenKind.Equals);
            var value = ParseExpression();
            assignments.Add(new WithPropertyAssignmentNode(
                property.Span.Union(value.Span), TokenIdentifier(property), value));
            if (!Match(TokenKind.Comma))
                break;
        }
        var close = Expect(TokenKind.CloseBrace);
        return new WithExpressionNode(target.Span.Union(close.Span), target, assignments);
    }

    #region Extended Features Parsing

    /// <summary>
    /// Parses an ASSUME assumption declaration.
    /// §ASSUME[env] "Database connection pool initialized"
    /// </summary>
    private AssumeNode ParseAssume()
    {
        var startToken = Expect(TokenKind.Assume);
        var attrs = ParseAttributes();
        var category = AttributeHelper.InterpretAssumeAttributes(attrs);

        // Expect a string description
        string description = "";
        if (Check(TokenKind.StrLiteral))
        {
            var strToken = Advance();
            description = (string)strToken.Value!;
        }
        else
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "ASSUME", "description");
        }

        return new AssumeNode(startToken.Span, category, description, attrs);
    }

    #endregion

    #region Enum and Enum Extensions

    /// <summary>
    /// Parses an enum definition.
    /// §EN{id:Name} or §EN{id:Name:underlyingType}
    ///   Red
    ///   Green = 1
    ///   Blue = 2
    /// §/EN{id}
    /// </summary>
    private EnumDefinitionNode ParseEnumDefinition()
    {
        var startToken = Expect(TokenKind.Enum);
        var attrs = ParseDeclarationAttributes(startToken);
        ValidateIdentityFreeHeader(startToken, attrs, 3);
        var csharpAttrs = ParseCSharpAttributes();

        // Positional: [id:name] or [id:name:vis] or [id:name:underlyingType] or [id:name:vis:underlyingType]
        var id = GenerateParserAutoId("e");
        var name = attrs["_pos0"] ?? "";
        var enumNameKey = "_pos0";
        var pos2 = attrs["_pos1"];
        var pos3 = attrs["_pos2"];

        // Disambiguate pos2: visibility keyword vs underlying type
        Visibility visibility = Visibility.Public;
        string? underlyingType;
        if (pos3 != null)
        {
            // 4 positionals: §EN{id:Name:vis:underlyingType}
            visibility = ParseVisibility(pos2, startToken.Span);
            underlyingType = pos3;
        }
        else if (pos2 != null && IsVisibilityKeyword(pos2))
        {
            // 3 positionals with visibility: §EN{id:Name:vis}
            visibility = ParseVisibility(pos2, startToken.Span);
            underlyingType = null;
        }
        else
        {
            // 3 positionals with underlying type or 2 positionals: §EN{id:Name} or §EN{id:Name:underlyingType}
            underlyingType = pos2;
        }

        if (string.IsNullOrEmpty(name))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "EN", "name");
        }

        var members = new List<EnumMemberNode>();

        // Parse enum members until we hit the closing tag. #1485: members are
        // the lines indented under the §EN (or on its own line); an empty enum
        // ends at a same-column sibling.
        var bodyOwner = BeginBlockBody(startToken, TokenKind.EndEnum, id);
        while (!IsAtEnd && !IsBlockEndAtIndent(TokenKind.EndEnum, startToken)
               && IsInDeclarationBody(bodyOwner, interopIsSibling: true))
        {
            if (ConsumeContinuationDedent(startToken))
                continue;

            // Parse optional C# attributes before enum member (e.g., [@Obsolete])
            var memberAttrs = ParseCSharpAttributes();

            // Each enum member is an identifier optionally followed by = value
            // Handle @keyword enum members (e.g., @decimal, @event) and /Name members
            if (Check(TokenKind.At))
            {
                Advance(); // consume @
            }
            if (Check(TokenKind.Slash))
            {
                Advance(); // consume / (some enums have /MemberName format)
            }
            if (Check(TokenKind.Identifier) || Current.IsKeyword)
            {
                var memberStartToken = Current;
                var memberNameToken = Advance();
                var memberName = memberNameToken.Value as string ?? memberNameToken.Text;
                string? memberValue = null;

                // Check for = value
                if (Check(TokenKind.Equals))
                {
                    Advance(); // consume =
                    memberValue = ParseEnumMemberValue();
                }

                var memberSpan = memberStartToken.Span.Union(Current.Span);
                members.Add(new EnumMemberNode(memberSpan, memberName, memberValue, memberAttrs, memberNameToken.Span));
            }
            else
            {
                _diagnostics.ReportUnexpectedToken(Current.Span, "enum member name", Current.Kind);
                Advance();
            }
        }

        while (ConsumeContinuationDedent(startToken))
        {
        }
        var endToken = ExpectOwnedBlockEnd(TokenKind.EndEnum, bodyOwner);
        var endAttrs = ParseAttributes();


        var span = startToken.Span.Union(endToken.Span);
        return WithDocumentation(new EnumDefinitionNode(
            span,
            id,
            name,
            underlyingType,
            members,
            attrs,
            csharpAttrs,
            visibility,
            GetIdentifierSpan(attrs, enumNameKey, name)), startToken);
    }

    /// <summary>
    /// Parses an enum member value, which can be a single token or a binary expression chain
    /// (e.g., <c>Ignore | Populate</c>, <c>1 &lt;&lt; 2</c>, <c>(Read | Write) &amp; ~Execute</c>).
    /// </summary>
    private string? ParseEnumMemberValue()
    {
        var value = ParseEnumOperand();
        if (value == null)
            return null;

        // Extend with binary operators: |, &, ^, <<, >>, +, -, *, /
        while (Check(TokenKind.Pipe) || Check(TokenKind.Amp) || Check(TokenKind.Caret) ||
               Check(TokenKind.LessLess) || Check(TokenKind.GreaterGreater) ||
               Check(TokenKind.Plus) || Check(TokenKind.Minus) ||
               Check(TokenKind.Star) || Check(TokenKind.Slash))
        {
            var op = Advance().Text;
            var rhs = ParseEnumOperand();
            if (rhs == null)
                break;
            value = $"{value} {op} {rhs}";
        }

        return value;
    }

    /// <summary>
    /// Parses a single operand in an enum value expression: integer literal, identifier,
    /// negative integer, bitwise complement (~), parenthesized subexpression, or
    /// function-call-like expressions (e.g., <c>unchecked((int)0xFFFF)</c>).
    /// </summary>
    private string? ParseEnumOperand()
    {
        if (Check(TokenKind.IntLiteral) || Check(TokenKind.CharLiteral))
        {
            return Advance().Text;
        }
        if (Check(TokenKind.StrLiteral))
        {
            var val = Advance().Value as string ?? "";
            return $"'{val}'";
        }
        if (Check(TokenKind.Identifier))
        {
            var text = Advance().Text;
            // Handle function-call-like expressions: unchecked(...), nameof(...)
            if (Check(TokenKind.OpenParen))
            {
                var args = ConsumeBalancedParens();
                return $"{text}{args}";
            }
            // Handle dotted identifiers: SomeType.Value
            while (Check(TokenKind.Dot) && Peek(1).Kind == TokenKind.Identifier)
            {
                Advance(); // consume .
                text += $".{Advance().Text}";
            }
            return text;
        }
        if (Check(TokenKind.Minus) && Peek(1).Kind == TokenKind.IntLiteral)
        {
            Advance(); // consume -
            return $"-{Advance().Text}";
        }
        if (Check(TokenKind.Tilde))
        {
            Advance(); // consume ~
            var inner = ParseEnumOperand();
            return inner != null ? $"~{inner}" : null;
        }
        if (Check(TokenKind.OpenParen))
        {
            // Could be a parenthesized subexpression or a cast: (int)value
            var saved = _position;
            Advance(); // consume (
            var inner = ParseEnumMemberValue(); // recursive -- full expression inside parens
            if (inner != null && Check(TokenKind.CloseParen))
            {
                Advance(); // consume )
                // Check if this is a cast: (type)operand -- if next token can be an operand
                // Only treat as cast if inner looks like a type name (no operators, not a number)
                var looksLikeCast = IsEnumOperandStart()
                    && inner != null
                    && !char.IsDigit(inner[0]) && !inner.StartsWith("0x") && !inner.StartsWith("0X")
                    && !inner.Contains("<<") && !inner.Contains(">>")
                    && !inner.Contains("|") && !inner.Contains("&")
                    && !inner.Contains("^") && !inner.Contains("+")
                    && !inner.Contains("-") && !inner.Contains("~");
                if (looksLikeCast)
                {
                    var castTarget = ParseEnumOperand();
                    return castTarget != null ? $"({inner}){castTarget}" : $"({inner})";
                }
                return $"({inner})";
            }
            // Failed to parse inside parens -- restore and fall through
            _position = saved;
        }
        return null;
    }

    /// <summary>
    /// Returns true if the current token can start an enum operand.
    /// </summary>
    private bool IsEnumOperandStart()
    {
        return Check(TokenKind.IntLiteral) || Check(TokenKind.CharLiteral) || Check(TokenKind.Identifier) ||
               Check(TokenKind.Tilde) || Check(TokenKind.OpenParen) ||
               (Check(TokenKind.Minus) && Peek(1).Kind == TokenKind.IntLiteral);
    }

    /// <summary>
    /// Consumes a balanced parenthesized token sequence including the outer parens,
    /// returning the reconstructed text (e.g., <c>((int)0xFFFF)</c>).
    /// Uses source positions to preserve original spacing between tokens.
    /// </summary>
    private string ConsumeBalancedParens()
    {
        var sb = new System.Text.StringBuilder();
        var openParen = Advance(); // consume opening (
        sb.Append(openParen.Text);
        int depth = 1;
        var prevEnd = openParen.Span.Start + openParen.Span.Length;
        while (!IsAtEnd && depth > 0)
        {
            if (Check(TokenKind.OpenParen)) depth++;
            else if (Check(TokenKind.CloseParen)) depth--;
            if (depth > 0)
            {
                if (Current.Span.Start > prevEnd)
                    sb.Append(' ');
                prevEnd = Current.Span.Start + Current.Span.Length;
                sb.Append(Current.Text);
                Advance();
            }
        }
        if (Check(TokenKind.CloseParen))
        {
            if (Current.Span.Start > prevEnd)
                sb.Append(' ');
            sb.Append(Advance().Text); // consume closing )
        }
        return sb.ToString();
    }

    #endregion

    #region Class Modifier Helpers

    private static bool IsVisibilityKeyword(string value)
    {
        // #1607: one table (TryParseVisibilityWord) decides what a visibility word is.
        return TryParseVisibilityWord(value, out _);
    }

    private static readonly HashSet<string> VisibilityKeywords = new(StringComparer.Ordinal)
    {
        "public", "protected", "private", "internal", "private-protected", "protected-internal"
    };

    private static readonly HashSet<string> ClassModifierKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "sealed", "static",
        "partial", "struct", "readonly",
        "public", "protected", "private", "internal", "private-protected", "protected-internal"
    };

    #endregion

    #region Compact Syntax Helpers

    private int _autoIdCounter;

    /// <summary>
    /// Generates a unique auto-assigned ID for compact syntax forms that omit the ID.
    /// Format: prefix + "_auto" + counter (e.g., "f_auto1", "m_auto2", "p_auto3").
    /// </summary>
    private string GenerateParserAutoId(string prefix)
    {
        _autoIdCounter++;
        return $"{prefix}_auto{_autoIdCounter}";
    }



    /// <summary>
    /// Known accessor keywords used in compact property shorthand.
    /// Includes short form (g, s, i) and long form (get, set, init) with optional
    /// visibility prefixes (priget, proset, intinit).
    /// </summary>
    private static readonly HashSet<string> AccessorKeywords = new(StringComparer.Ordinal)
    {
        "g", "s", "i", "gs", "gi", "si", "gsi", "gis",
        "get", "set", "init",
        "priget", "proget", "intget", "privprotget",
        "priset", "proset", "intset", "privprotset",
        "priinit", "proinit", "intinit"
    };

    /// <summary>
    /// Determines if a string is a compact accessor shorthand.
    /// Recognized patterns:
    /// - Short form: "g", "gs", "gi", "gsi", "gis", "s", "si", "i"
    /// - Long form: "get", "set", "init", "priget", "proset", etc.
    /// - Comma-separated: "get,priset", "get,init", "get,set"
    /// </summary>
    private static bool IsAccessorShorthand(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        // Check comma-separated list: "get,priset" etc.
        var parts = value.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (!AccessorKeywords.Contains(trimmed))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Parses a compact accessor shorthand into getter, setter, and initer accessor nodes.
    /// Handles both short form (gs, g, gi) and long form (get, priset, init) patterns.
    /// Comma-separated accessors: "get,priset", "get,init"
    /// </summary>
    private (PropertyAccessorNode? getter, PropertyAccessorNode? setter, PropertyAccessorNode? initer)
        ParseAccessorShorthand(string spec, TextSpan span, Visibility ownerVisibility)
    {
        PropertyAccessorNode? getter = null;
        PropertyAccessorNode? setter = null;
        PropertyAccessorNode? initer = null;

        // Split on comma for multi-accessor specs: "get,priset" etc.
        var parts = spec.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();

            // Long form with optional visibility prefix
            if (trimmed.EndsWith("get"))
            {
                var vis = ExtractAccessorVisibility(trimmed, "get");
                getter = CreateAutoAccessor(PropertyAccessorNode.AccessorKind.Get, span, vis);
            }
            else if (trimmed.EndsWith("set"))
            {
                var vis = ExtractAccessorVisibility(trimmed, "set");
                setter = CreateAutoAccessor(PropertyAccessorNode.AccessorKind.Set, span, vis);
            }
            else if (trimmed.EndsWith("init"))
            {
                var vis = ExtractAccessorVisibility(trimmed, "init");
                initer = CreateAutoAccessor(PropertyAccessorNode.AccessorKind.Init, span, vis);
            }
            else
            {
                // Short form: each character is an accessor
                foreach (var ch in trimmed)
                {
                    switch (ch)
                    {
                        case 'g':
                            getter = CreateAutoAccessor(PropertyAccessorNode.AccessorKind.Get, span, null);
                            break;
                        case 's':
                            setter = CreateAutoAccessor(PropertyAccessorNode.AccessorKind.Set, span, null);
                            break;
                        case 'i':
                            initer = CreateAutoAccessor(PropertyAccessorNode.AccessorKind.Init, span, null);
                            break;
                    }
                }
            }
        }

        return (getter, setter, initer);
    }

    /// <summary>
    /// Extracts optional visibility prefix from accessor keyword (e.g., "priset" -> Private, "proget" -> Protected).
    /// </summary>
    private static Visibility? ExtractAccessorVisibility(string keyword, string accessorSuffix)
    {
        var prefix = keyword.Substring(0, keyword.Length - accessorSuffix.Length);
        if (string.IsNullOrEmpty(prefix))
            return null;

        return prefix switch
        {
            "pri" or "priv" or "private" => Visibility.Private,
            "privprot" => Visibility.PrivateProtected,
            "pro" or "prot" or "protected" => Visibility.Protected,
            "int" or "internal" => Visibility.Internal,
            _ => null
        };
    }

    /// <summary>
    /// Creates an auto-implemented property accessor node with empty body.
    /// </summary>
    private static PropertyAccessorNode CreateAutoAccessor(
        PropertyAccessorNode.AccessorKind kind, TextSpan span, Visibility? visibility)
    {
        return new PropertyAccessorNode(
            span, kind, visibility,
            Array.Empty<RequiresNode>(),
            Array.Empty<StatementNode>(),
            new AttributeCollection());
    }

    /// <summary>
    /// Attempts to parse an inline signature after a tag's attributes.
    /// Inline signatures use parentheses for parameters and an arrow for return type:
    ///   §F{name:pub}(type:name, type:name) -> returnType
    /// Both colon-separated (type:name) and space-separated (type name) formats are supported.
    /// If no opening paren is found, this method does nothing.
    /// </summary>
    private void TryParseInlineSignature(TextSpan span, List<ParameterNode> parameters, ref OutputNode? output,
        bool brackets = false)
    {
        var open = brackets ? TokenKind.OpenBracket : TokenKind.OpenParen;
        var close = brackets ? TokenKind.CloseBracket : TokenKind.CloseParen;
        if (!Check(open))
        {
            _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken,
                brackets ? "An indexer requires a bracketed type:name parameter list."
                    : "A canonical callable requires an inline parameter list, including '()' for no parameters.");
            return;
        }

        Advance(); // consume (

        // Parse comma-separated parameters: type:name, type:name, ...
        if (!Check(close))
        {
            do
            {
                // Parse optional parameter modifier (this, ref, out, in, params)
                var modifier = ParameterModifier.None;
                while (Current.Text is "this" or "ref" or "out" or "in" or "params")
                {
                    var modifierToken = Advance();
                    modifier |= ParseParameterModifiers(modifierToken.Text, modifierToken.Span);
                }

                var typeStartPosition = _position;
                var paramType = ReadInlineTypeToken();
                var parameterTypeSpan = GetConsumedSpan(typeStartPosition);
                var paramName = "";
                var parameterIdentifierSpan = span;

                // Check for colon separator (type:name format) or space separator (type name)
                if (Check(TokenKind.Colon))
                {
                    Advance(); // consume :
                    if (Check(TokenKind.Identifier))
                    {
                        var nameToken = Advance();
                        paramName = nameToken.Value as string ?? nameToken.Text;
                        parameterIdentifierSpan = nameToken.Span;
                    }

                    if (Check(TokenKind.Colon))
                    {
                        _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken,
                            "Parameter modifiers precede the type in a canonical signature.");
                        Advance();
                        if (Check(TokenKind.Identifier)) Advance();
                    }
                }
                else
                {
                    _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken,
                        "Expected a type:name parameter.");
                    if (Check(TokenKind.Identifier))
                    {
                        var nameToken = Advance();
                        paramName = nameToken.Value as string ?? nameToken.Text;
                        parameterIdentifierSpan = nameToken.Span;
                    }
                }

                var csharpAttributes = ParseCSharpAttributes();

                // Position 5 (§3.3): (Func<i32,i32>:f §E{cw}, i32:v). The inline
                // parameter list is one of the four positions with no §E arm behind it,
                // so a non-adjacent §E here is Calor0405 with recovery rather than the
                // twelve-diagnostic cascade of the executed X9c/Z3 baselines.
                var inlineRow = TryParseSameLineRow(EffectRowPosition.Parameter);

                InlineRefinementInfo? inlineRefinement = null;
                if (Match(TokenKind.Pipe))
                {
                    var saved = _insideRefinementPredicate;
                    _insideRefinementPredicate = true;
                    try
                    {
                        inlineRefinement = new InlineRefinementInfo(paramType, ParseExpression());
                    }
                    finally
                    {
                        _insideRefinementPredicate = saved;
                    }
                }

                // Parse optional default value: = expression
                ExpressionNode? defaultValue = null;
                if (Match(TokenKind.Equals))
                {
                    defaultValue = ParseExpression();
                }

                var paramAttrs = new AttributeCollection();
                paramAttrs.Add("_pos0", paramType);
                paramAttrs.Add("_pos1", paramName);
                parameters.Add(new ParameterNode(
                    span,
                    paramName,
                    paramType,
                    modifier,
                    paramAttrs,
                    csharpAttributes,
                    defaultValue,
                    inlineRefinement,
                    identifierSpan: parameterIdentifierSpan,
                    typeNameSpan: parameterTypeSpan,
                    row: inlineRow));
            }
            while (Match(TokenKind.Comma));
        }

        Expect(close);

        // Parse optional return type: -> type or → type
        if (Check(TokenKind.Arrow))
        {
            Advance(); // consume ->
            // Handle ref/in/out return type modifiers
            var prefix = "";
            if (Current.Text is "ref" or "in" or "out" or "scoped")
            {
                prefix = Current.Text + " ";
                Advance();
                // Handle 'ref readonly'
                if (prefix == "ref " && Check(TokenKind.Identifier) && Current.Text == "readonly")
                {
                    prefix = "ref readonly ";
                    Advance();
                }
            }
            var typeStartPosition = _position;
            var returnType = prefix + ReadInlineTypeToken();

            // Position 6, arrow spelling (§3.3): ) -> Func<i32> §E{cw}. A §E on a later
            // line is left alone: that is the 2948-occurrence / 471-file compact corpus
            // form and it must keep meaning the declaration's own row.
            var arrowRow = TryParseSameLineRow(EffectRowPosition.Return);
            output = new OutputNode(span, returnType, GetConsumedSpan(typeStartPosition), arrowRow);
        }
    }

    private TextSpan GetConsumedSpan(int startPosition)
    {
        if (_position <= startPosition)
            return TextSpan.Empty;

        return _tokens[startPosition].Span.Union(_tokens[_position - 1].Span);
    }

    /// <summary>
    /// Reads a type token for inline signatures. Handles simple types (i32, str, bool),
    /// nullable types (i32?), array types ([i32]), and generic types (List&lt;i32&gt;).
    /// </summary>
    private string ReadInlineTypeToken(bool validateCanonicalSpelling = true)
    {
        var startPosition = _position;
        int pendingGreaters = 0;
        var type = ReadInlineTypeToken(ref pendingGreaters);
        if (validateCanonicalSpelling)
            ValidateCanonicalTypeSpelling(type, GetConsumedSpan(startPosition));
        return type;
    }

    private void ValidateCanonicalTypeSpelling(string type, TextSpan span)
    {
        if (type == "void") return;
        if (type.Length > 0 && type.AsSpan().IndexOfAny("<[?(* ".AsSpan()) < 0)
        {
            if (!type.StartsWith('@') && !type.Contains('.') && !type.Contains("::", StringComparison.Ordinal))
            {
                var canonical = Migration.TypeMapper.CalorToCSharp(type);
                if (canonical != type)
                    _diagnostics.ReportError(span, DiagnosticCode.UnexpectedToken,
                        $"Type alias '{type}' is retired. Use the C# type '{canonical}'.");
            }
            return;
        }
        var syntax = SyntaxFactory.ParseTypeName(type);
        if (syntax.ContainsDiagnostics)
        {
            _diagnostics.ReportError(span, DiagnosticCode.UnexpectedToken,
                "Use a C# type spelling with suffix arrays and nullability, such as 'int[]' or 'string?'.");
            return;
        }
        foreach (var name in syntax.DescendantNodesAndSelf().OfType<SimpleNameSyntax>())
        {
            if (name.Identifier.Text.StartsWith('@')
                || name.Parent is QualifiedNameSyntax or AliasQualifiedNameSyntax)
                continue;
            var spelling = name.Identifier.Text;
            var canonical = Migration.TypeMapper.CalorToCSharp(spelling);
            if (canonical != spelling)
                _diagnostics.ReportError(span, DiagnosticCode.UnexpectedToken,
                    $"Type alias '{spelling}' is retired. Use the C# type '{canonical}'.");
        }
    }

    /// <summary>
    /// Internal overload that tracks pending '>' tokens from consumed '>>' (GreaterGreater) tokens.
    /// This handles nested generics like IEnumerable&lt;IGrouping&lt;i32, i32&gt;&gt; where the lexer
    /// tokenizes '>>' as a single GreaterGreater token.
    /// </summary>
    private string ReadInlineTypeToken(ref int pendingGreaters)
    {
        var sb = new System.Text.StringBuilder();

        // Handle nullable prefix (may be doubled: ??Type for Nullable<Nullable<T>> from converter)
        // ?? is lexed as NullCoalesce token, so check for that too
        if (Check(TokenKind.NullCoalesce))
        {
            sb.Append("??");
            Advance();
        }
        else
        {
            while (Check(TokenKind.Question))
            {
                sb.Append('?');
                Advance();
            }
        }

        // Handle ref/in/out/scoped modifiers (e.g., ?ref T, ref readonly T)
        if (Current.Text is "ref" or "in" or "out" or "scoped")
        {
            sb.Append(Advance().Text);
            sb.Append(' ');
            if (Check(TokenKind.Identifier) && Current.Text == "readonly")
            {
                sb.Append(Advance().Text);
                sb.Append(' ');
            }
        }

        // Handle array type [T]
        if (Check(TokenKind.OpenBracket))
        {
            sb.Append('[');
            Advance();
            sb.Append(ReadInlineTypeToken(ref pendingGreaters));
            if (Check(TokenKind.CloseBracket))
            {
                sb.Append(']');
                Advance();
            }
            return sb.ToString();
        }

        // Handle tuple type (T1, T2, ...) or (T1 Name1, T2 Name2, ...)
        if (Check(TokenKind.OpenParen))
        {
            sb.Append('(');
            Advance(); // consume (

            sb.Append(ReadInlineTypeToken(ref pendingGreaters));
            // Check for optional element name (may be a keyword like 'delegate')
            if ((Check(TokenKind.Identifier) || Current.IsKeyword) && !Check(TokenKind.Comma) && !Check(TokenKind.CloseParen))
            {
                sb.Append(' ');
                sb.Append(Advance().Text);
            }

            while (Check(TokenKind.Comma))
            {
                sb.Append(", ");
                Advance(); // consume ,
                sb.Append(ReadInlineTypeToken(ref pendingGreaters));
                // Check for optional element name (may be a keyword like 'delegate')
                if ((Check(TokenKind.Identifier) || Current.IsKeyword) && !Check(TokenKind.Comma) && !Check(TokenKind.CloseParen))
                {
                    sb.Append(' ');
                    sb.Append(Advance().Text);
                }
            }

            if (Check(TokenKind.CloseParen))
            {
                sb.Append(')');
                Advance();
            }

            ReadInlineTypeSuffixes(sb);

            return sb.ToString();
        }

        // Read the base type name
        if (Check(TokenKind.Identifier) || Current.IsKeyword)
        {
            sb.Append(Advance().Text);
        }
        else if (Check(TokenKind.IntLiteral))
        {
            // For types like "i32", "f64" that might lex as identifier
            sb.Append(Advance().Text);
        }

        if (sb.Length > 0 && sb[^1] == '.' && Check(TokenKind.Identifier)
            && Current.Text.StartsWith('@') && Current.Span.Line == PreviousToken.Span.Line)
            sb.Append(Advance().Text);

        // Handle alias-qualified names: global::System.Namespace.Type
        if (Check(TokenKind.Colon) && Peek(1).Kind == TokenKind.Colon)
        {
            sb.Append("::");
            Advance(); // consume first :
            Advance(); // consume second :
            if (Check(TokenKind.Identifier) || Current.IsKeyword)
            {
                sb.Append(Advance().Text);
            }
        }

        // Handle dot-separated qualified names: System.Threading.CancellationToken
        while (Check(TokenKind.Dot) && (Peek(1).Kind == TokenKind.Identifier || Peek(1).IsKeyword))
        {
            sb.Append('.');
            Advance(); // consume .
            sb.Append(Advance().Text);
        }

        // Handle pointer suffix: Type*, Type**
        while (Check(TokenKind.Star) || Check(TokenKind.StarStar))
        {
            if (Check(TokenKind.StarStar)) { sb.Append("**"); Advance(); continue; }
            sb.Append('*');
            Advance();
        }

        // Handle function pointer calling convention: delegate* unmanaged<T, void>
        // or delegate* unmanaged[Cdecl]<T, void>
        if (sb.Length > 0 && sb.ToString().EndsWith('*')
            && Check(TokenKind.Identifier) && (Peek(1).Kind == TokenKind.Less || Peek(1).Kind == TokenKind.OpenBracket))
        {
            sb.Append(' ');
            sb.Append(Advance().Text); // consume calling convention identifier (e.g., "unmanaged")
            // Handle optional [CallingConvention, ...] list
            if (Check(TokenKind.OpenBracket))
            {
                sb.Append('[');
                Advance();
                while (!IsAtEnd && !Check(TokenKind.CloseBracket))
                {
                    sb.Append(Current.Text);
                    Advance();
                }
                if (Check(TokenKind.CloseBracket))
                {
                    sb.Append(']');
                    Advance();
                }
            }
            // Fall through to generic parameter handling below
        }

        // Handle nullable suffix
        if (Check(TokenKind.Question))
        {
            sb.Append('?');
            Advance();
        }

        // Handle generic parameters: Type<T, U>
        if (Check(TokenKind.Less))
        {
            sb.Append('<');
            Advance();
            sb.Append(ReadInlineTypeToken(ref pendingGreaters));
            while (Check(TokenKind.Comma))
            {
                sb.Append(',');
                Advance();
                sb.Append(ReadInlineTypeToken(ref pendingGreaters));
            }
            if (pendingGreaters > 0)
            {
                // A child already consumed a GreaterGreater token; use the pending '>'
                sb.Append('>');
                pendingGreaters--;
            }
            else if (Check(TokenKind.Greater))
            {
                sb.Append('>');
                Advance();
            }
            else if (Check(TokenKind.GreaterGreater))
            {
                // '>>' token: consume it, close this generic level, and leave one '>' for the parent
                sb.Append('>');
                Advance();
                pendingGreaters++;
            }
        }

        // Handle nullable suffix after generic close: Task<T>?, Action<T>?
        if (Check(TokenKind.Question))
        {
            sb.Append('?');
            Advance();
        }

        // Handle nested type suffix after generic close: List<T>.Enumerator
        while (Check(TokenKind.Dot) && (Peek(1).Kind == TokenKind.Identifier || Peek(1).IsKeyword))
        {
            sb.Append('.');
            Advance(); // consume .
            sb.Append(Advance().Text);
            // The nested type might itself be generic: Outer<T>.Inner<U>
            if (Check(TokenKind.Less))
            {
                sb.Append('<');
                Advance();
                sb.Append(ReadInlineTypeToken(ref pendingGreaters));
                while (Check(TokenKind.Comma))
                {
                    sb.Append(", ");
                    Advance();
                    sb.Append(ReadInlineTypeToken(ref pendingGreaters));
                }
                if (pendingGreaters > 0)
                {
                    sb.Append('>');
                    pendingGreaters--;
                }
                else if (Check(TokenKind.Greater))
                {
                    sb.Append('>');
                    Advance();
                }
                else if (Check(TokenKind.GreaterGreater))
                {
                    sb.Append('>');
                    Advance();
                    pendingGreaters++;
                }
            }
        }

        ReadInlineTypeSuffixes(sb);
        return sb.ToString();
    }

    private void ReadInlineTypeSuffixes(System.Text.StringBuilder sb, bool allowNullable = true)
    {
        while (Check(TokenKind.OpenBracket) || (allowNullable && Check(TokenKind.Question)))
        {
            if (Match(TokenKind.Question))
            {
                sb.Append('?');
            }
            else if (Peek(1).Kind == TokenKind.CloseBracket)
            {
                sb.Append("[]");
                Advance(); // consume [
                Advance(); // consume ]
            }
            else if (Peek(1).Kind == TokenKind.Comma)
            {
                sb.Append('[');
                Advance(); // consume [
                while (Check(TokenKind.Comma))
                {
                    sb.Append(',');
                    Advance();
                }
                Expect(TokenKind.CloseBracket);
                sb.Append(']');
            }
            else break;
        }
    }

    #endregion
    #region Unsafe/Low-Level Parsing

    /// <summary>
    /// Parses §SYNC{id} (expr) ... §/SYNC{id}
    /// </summary>
    private SyncBlockNode ParseSyncBlock()
    {
        var startToken = Expect(TokenKind.SyncBlock);
        RejectLegacyLoopHeader(startToken, "lock expression");
        var id = GenerateParserAutoId("sync");
        var lockExpr = ParseExpression(allowSpacedCall: false);

        // #1485: the body owns only lines indented under the opener.
        var bodyOwner = BeginBlockBody(startToken, TokenKind.EndSyncBlock, id);
        var body = ParseOwnedStatementBlock(bodyOwner, TokenKind.EndSyncBlock);
        if (IsOwnedDedent(bodyOwner))
        {
            ExpectBlockEnd(TokenKind.EndSyncBlock);
            ParseAttributes(); // consume optional {id} on closing tag
        }

        return new SyncBlockNode(startToken.Span, id, lockExpr, body);
    }

    /// <summary>
    /// Parses §UNSAFE{id} ... §/UNSAFE{id}
    /// </summary>
    private UnsafeBlockNode ParseUnsafeBlock()
    {
        var startToken = Expect(TokenKind.Unsafe);
        RejectLegacyLoopHeader(startToken, "unsafe with an indented body");
        var id = GenerateParserAutoId("unsafe");

        // #1485: the body owns only lines indented under the opener.
        var bodyOwner = BeginBlockBody(startToken, TokenKind.EndUnsafe, id);
        var body = ParseOwnedStatementBlock(bodyOwner, TokenKind.EndUnsafe);
        if (IsOwnedDedent(bodyOwner))
        {
            ExpectBlockEnd(TokenKind.EndUnsafe);
            ParseAttributes(); // consume optional {id} on closing tag
        }

        return new UnsafeBlockNode(startToken.Span, id, body);
    }

    /// <summary>
    /// Parses §FIXED{id:ptr:type:init} ... §/FIXED{id}
    /// </summary>
    private FixedStatementNode ParseFixedStatement()
    {
        var startToken = Expect(TokenKind.Fixed);
        RejectLegacyLoopHeader(startToken, "fixed pointerType:name = initializer");
        var id = GenerateParserAutoId("fixed");
        var typeSpan = Current.Span;
        var pointerType = ParseValue();
        Expect(TokenKind.Colon);
        var pointer = Expect(TokenKind.Identifier);
        Expect(TokenKind.Equals);
        var initializer = ParseExpression(allowSpacedCall: false);

        // #1485: the body owns only lines indented under the opener.
        var bodyOwner = BeginBlockBody(startToken, TokenKind.EndFixed, id);
        var body = ParseOwnedStatementBlock(bodyOwner, TokenKind.EndFixed);
        if (IsOwnedDedent(bodyOwner))
        {
            ExpectBlockEnd(TokenKind.EndFixed);
            ParseAttributes(); // consume optional {id} on closing tag
        }

        return new FixedStatementNode(
            startToken.Span,
            id,
            pointer.Text,
            pointerType,
            initializer,
            body,
            typeSpan);
    }

    #endregion

    /// <summary>
    /// The simple-name roots of member-access chains (<c>root.Member…</c>) in
    /// <paramref name="module"/> that the parser reads as member accesses when written
    /// unspaced: a value in scope, or a source class of the module with that member. The
    /// Calor emitter prints any other chain's receiver in parentheses, because an unspaced
    /// <c>Type.Static.Member</c> would read back as one dotted name.
    /// </summary>
    internal static IReadOnlySet<ReferenceNode> FindMemberAccessRoots(ModuleNode module)
        => DottedValueReadResolver.FindRoots(module);

    /// <summary>
    /// Decides which dotted names (<c>x.Field</c>) are member reads. C# simple-name lookup
    /// finds a value before any namespace or type, so a first segment that names a value in
    /// scope makes a member read: a local, loop, catch or pattern variable, a parameter (with
    /// an accessor's <c>value</c> and a postcondition's <c>result</c>), or a field, property
    /// or event of an enclosing type, of its other partial parts, or of a base type declared
    /// in the module. A first segment that names a top-level source class or module of this
    /// module, followed by one of its fields, properties or events, is a member read on the
    /// type, exactly as the spaced <c>Type . Member</c>. Any other first segment (a
    /// namespace, a metadata type, an enum, a function of a module, a nested type, or a
    /// member this module cannot see) keeps the dotted name's meaning. A nested type of an
    /// enclosing class shadows an outer value of the same name, as in C#. Scopes follow the source blocks; a pattern variable
    /// is visible in the rest of the statement list that holds its test.
    /// </summary>
    private sealed class DottedValueReadResolver
    {
        private readonly IReadOnlyDictionary<ReferenceNode, int>? _candidates;
        private readonly Dictionary<string, List<ClassDefinitionNode>> _classesByName = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _typeMembers = new(StringComparer.Ordinal);
        private readonly List<Dictionary<string, string?>?> _scopes = [];
        private readonly Stack<int> _containers = new();
        private readonly HashSet<int> _valueReads = [];
        private readonly HashSet<ReferenceNode> _roots = new(ReferenceEqualityComparer.Instance);

        private DottedValueReadResolver(ModuleNode module, IReadOnlyDictionary<ReferenceNode, int>? candidates)
        {
            _candidates = candidates;
            foreach (var type in Analysis.CallGraphAnalysis.EnumerateClasses(module))
            {
                if (!_classesByName.TryGetValue(type.Name, out var declarations))
                    _classesByName[type.Name] = declarations = [];
                declarations.Add(type);
            }
            var topLevel = module.Classes.Concat(module.TypePreprocessorBlocks
                .SelectMany(block => EnumerateBranches(block).SelectMany(branch => branch.Classes)));
            foreach (var type in topLevel)
            {
                if (!_typeMembers.TryGetValue(type.Name, out var members))
                    _typeMembers[type.Name] = members = new HashSet<string>(StringComparer.Ordinal);
                foreach (var (name, _) in ValueMembers(type))
                    members.Add(name);
            }
            _scopes.Add(null);
            _containers.Push(0);
            Walk(module);
        }

        public static IReadOnlySet<int> Resolve(ModuleNode module, IReadOnlyDictionary<ReferenceNode, int> candidates)
            => new DottedValueReadResolver(module, candidates)._valueReads;

        public static IReadOnlySet<ReferenceNode> FindRoots(ModuleNode module)
            => new DottedValueReadResolver(module, null)._roots;

        private static IEnumerable<TypePreprocessorBlockNode> EnumerateBranches(TypePreprocessorBlockNode block)
        {
            for (var branch = block; branch != null; branch = branch.ElseBranch)
                yield return branch;
        }

        private void Walk(AstNode node)
        {
            if (node is ReferenceNode reference)
            {
                if (_candidates is not null && _candidates.TryGetValue(reference, out var start)
                    && IsMemberRead(reference.Name))
                    _valueReads.Add(start);
                return;
            }
            // A pattern's dotted names are constants (enum members, static fields); its
            // variables are declared by the case or test that owns it.
            if (node is PatternNode)
                return;
            if (_candidates is null && node is FieldAccessNode { Target: ReferenceNode root } member
                && !root.Name.Contains('.') && IsMemberRead($"{root.Name}.{member.FieldName}"))
                _roots.Add(root);

            var depth = _scopes.Count;
            var containers = _containers.Count;
            if (node is StatementNode)
                _containers.Push(depth - 1);
            _scopes.Add(null);
            if (node is FunctionNode or MethodNode or ConstructorNode or OperatorOverloadNode or IndexerNode
                or PropertyAccessorNode or EventDefinitionNode or LambdaExpressionNode
                or ClassFieldNode or PropertyNode or EnsuresNode or RequiresNode)
                _containers.Push(depth);
            DeclareOwn(node, depth);

            System.Reflection.PropertyInfo? group = null;
            var groupDepth = -1;
            foreach (var edge in Analysis.RecursiveAstWalker.GetAllChildEdges(node))
            {
                if (!ReferenceEquals(edge.Property, group))
                {
                    if (groupDepth >= 0)
                        _scopes.RemoveRange(groupDepth, _scopes.Count - groupDepth);
                    group = edge.Property;
                    // Each statement list (a body, a branch, a case) is its own block.
                    groupDepth = typeof(AstNode).IsAssignableFrom(group.PropertyType) ? -1 : _scopes.Count;
                    if (groupDepth >= 0)
                        _scopes.Add(null);
                }
                Walk(edge.Node);
            }

            switch (node)
            {
                case BindStatementNode bind:
                    Declare(_containers.Peek(), bind.Name, bind.TypeName);
                    break;
                case UsingStatementNode { VariableName: { } resource } usingStatement:
                    Declare(_containers.Peek(), resource, usingStatement.VariableType);
                    break;
                case RawCSharpNode raw:
                    // A preserved C# statement can declare locals the following statements read.
                    if (SyntaxFactory.ParseStatement("{" + raw.CSharpCode + "\n}") is BlockSyntax block)
                        foreach (var local in block.Statements.OfType<LocalDeclarationStatementSyntax>())
                            foreach (var variable in local.Declaration.Variables)
                                Declare(_containers.Peek(), variable.Identifier.ValueText,
                                    local.Declaration.Type.IsVar ? null : local.Declaration.Type.ToString());
                    break;
            }
            _scopes.RemoveRange(depth, _scopes.Count - depth);
            while (_containers.Count > containers)
                _containers.Pop();
            // `x is T name` declares name for the rest of the enclosing statement list.
            if (node is IsPatternNode { VariableName: { } name } test)
                Declare(_containers.Peek(), name, test.TargetType);
        }

        private void DeclareOwn(AstNode node, int scope)
        {
            switch (node)
            {
                case ClassDefinitionNode type:
                    DeclareMembers(type, scope, new HashSet<ClassDefinitionNode>(ReferenceEqualityComparer.Instance));
                    break;
                case InterfaceDefinitionNode contract:
                    foreach (var property in contract.Properties)
                        Declare(scope, property.Name, property.TypeName);
                    break;
                case FunctionNode function:
                    DeclareParameters(scope, function.Parameters);
                    break;
                case MethodNode method:
                    DeclareParameters(scope, method.Parameters);
                    break;
                case ConstructorNode constructor:
                    DeclareParameters(scope, constructor.Parameters);
                    break;
                case OperatorOverloadNode op:
                    DeclareParameters(scope, op.Parameters);
                    break;
                case IndexerNode indexer:
                    DeclareParameters(scope, indexer.Parameters);
                    break;
                case PropertyAccessorNode { Kind: PropertyAccessorNode.AccessorKind.Set or PropertyAccessorNode.AccessorKind.Init }:
                case EventDefinitionNode:
                    Declare(scope, "value", null);
                    break;
                case EnsuresNode:
                    Declare(scope, "result", null);
                    break;
                case LambdaExpressionNode lambda:
                    foreach (var parameter in lambda.Parameters)
                        Declare(scope, parameter.Name, parameter.TypeName);
                    break;
                case ForStatementNode loop:
                    Declare(scope, loop.VariableName, null);
                    break;
                case ForeachStatementNode each:
                    Declare(scope, each.VariableName, each.VariableType);
                    if (each.IndexVariableName is { } index)
                        Declare(scope, index, null);
                    break;
                case DictionaryForeachNode pairs:
                    Declare(scope, pairs.KeyName, null);
                    Declare(scope, pairs.ValueName, null);
                    break;
                case CatchClauseNode { VariableName: { } exception } handler:
                    Declare(scope, exception, handler.ExceptionType);
                    break;
                case UsingStatementNode { VariableName: { } resource } usingStatement:
                    Declare(scope, resource, usingStatement.VariableType);
                    break;
                case FixedStatementNode pinned:
                    Declare(scope, pinned.PointerName, pinned.PointerType);
                    break;
                case ForallExpressionNode forall:
                    foreach (var variable in forall.BoundVariables)
                        Declare(scope, variable.Name, variable.TypeName);
                    break;
                case ExistsExpressionNode exists:
                    foreach (var variable in exists.BoundVariables)
                        Declare(scope, variable.Name, variable.TypeName);
                    break;
                case MatchCaseNode matchCase:
                    DeclarePatternVariables(scope, matchCase.Pattern);
                    break;
            }
        }

        private void DeclareMembers(ClassDefinitionNode type, int scope, HashSet<ClassDefinitionNode> visited)
        {
            if (!visited.Add(type))
                return;
            foreach (var (name, typeName) in ValueMembers(type))
                Declare(scope, name, typeName);
            // A nested type is a member too: it hides an enclosing class's value of that name.
            foreach (var nested in type.NestedClasses.Select(nested => nested.Name)
                         .Concat(type.NestedInterfaces.Select(nested => nested.Name))
                         .Concat(type.NestedEnums.Select(nested => nested.Name))
                         .Concat(type.NestedDelegates.Select(nested => nested.Name)))
                (_scopes[scope] ??= new Dictionary<string, string?>(StringComparer.Ordinal)).TryAdd(nested, NestedTypeMarker);
            if (!_classesByName.TryGetValue(type.Name, out var sameName))
                sameName = [];
            // The other parts of a partial class declared in this module.
            foreach (var part in sameName.Where(part => type.IsPartial && part.IsPartial))
                DeclareMembers(part, scope, visited);
            if (type.BaseClass is { } baseName
                && _classesByName.TryGetValue(SimpleTypeName(baseName), out var bases))
                foreach (var baseType in bases)
                    DeclareMembers(baseType, scope, visited);
        }

        private static IEnumerable<(string Name, string? TypeName)> ValueMembers(ClassDefinitionNode type)
        {
            foreach (var field in type.Fields.Concat(type.PreprocessorBlocks.SelectMany(PreprocessorFields)))
                yield return (field.Name, field.TypeName);
            foreach (var property in Analysis.CallGraphAnalysis.EnumerateProperties(type).Concat(type.RecordPositionalProperties))
                yield return (property.Name, property.TypeName);
            foreach (var evt in Analysis.CallGraphAnalysis.EnumerateEvents(type))
                yield return (evt.Name, evt.DelegateType);
        }

        private static IEnumerable<ClassFieldNode> PreprocessorFields(MemberPreprocessorBlockNode block)
        {
            for (var branch = block; branch != null; branch = branch.ElseBranch)
                foreach (var field in branch.Fields)
                    yield return field;
        }

        private void DeclareParameters(int scope, IEnumerable<ParameterNode> parameters)
        {
            foreach (var parameter in parameters)
                Declare(scope, parameter.Name, parameter.TypeName);
        }

        private void DeclarePatternVariables(int scope, AstNode pattern)
        {
            switch (pattern)
            {
                case VariablePatternNode variable when !variable.Name.Contains('.'):
                    Declare(scope, variable.Name, null);
                    break;
                case VarPatternNode variable when variable.Name != "_":
                    Declare(scope, variable.Name, null);
                    break;
                case TypePatternNode { BindingName: { } binding } typed:
                    Declare(scope, binding, typed.TypeName);
                    break;
            }
            foreach (var child in Analysis.RecursiveAstWalker.GetAllChildren(pattern))
                DeclarePatternVariables(scope, child);
        }

        private void Declare(int scope, string name, string? typeName)
            => (_scopes[scope] ??= new Dictionary<string, string?>(StringComparer.Ordinal))[name] = typeName;

        private const string NestedTypeMarker = "\0type";

        private bool IsMemberRead(string dottedName)
        {
            var segments = dottedName.Split('.');
            var head = segments[0];
            if (head.Length == 0 || segments.Length < 2 || segments[1].Length == 0)
                return false;
            for (var index = _scopes.Count - 1; index >= 0; index--)
            {
                if (_scopes[index] is { } scope && scope.TryGetValue(head, out var typeName))
                    // C#'s "Color Color" rule: when a value's type has the value's own name,
                    // `Color.Red` may still mean the type's static member. That stays a
                    // dotted name (today's type-first binding) rather than a member read.
                    return typeName is null
                        || !ReferenceEquals(typeName, NestedTypeMarker) && SimpleTypeName(typeName) != head;
            }
            return _typeMembers.TryGetValue(head, out var members) && members.Contains(segments[1]);
        }

        private static string SimpleTypeName(string typeName)
        {
            var name = typeName.TrimEnd('?');
            var generic = name.IndexOf('<');
            if (generic >= 0)
                name = name[..generic];
            return name[(name.LastIndexOf('.') + 1)..];
        }
    }
}
