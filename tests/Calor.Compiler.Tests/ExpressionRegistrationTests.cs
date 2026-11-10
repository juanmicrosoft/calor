using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class ExpressionRegistrationTests
{
    public enum ExpressionContext
    {
        Return,
        Binding,
        CallArgument,
        Nested,
    }

    private static readonly IReadOnlyDictionary<TokenKind, (string Source, string ExpectedType)> Cases =
        new Dictionary<TokenKind, (string, string)>
        {
            [TokenKind.IntLiteral] = ("1", nameof(IntLiteralNode)),
            [TokenKind.StrLiteral] = ("\"x\"", nameof(StringLiteralNode)),
            [TokenKind.CharLiteral] = ("'x'", nameof(CharOperationNode)),
            [TokenKind.BoolLiteral] = ("true", nameof(BoolLiteralNode)),
            [TokenKind.FloatLiteral] = ("1.5", nameof(FloatLiteralNode)),
            [TokenKind.DecimalLiteral] = ("DEC:1.5", nameof(DecimalLiteralNode)),
            [TokenKind.Identifier] = ("value", nameof(ReferenceNode)),
            [TokenKind.OpenParen] = ("(+ 1 2)", nameof(BinaryOperationNode)),
            [TokenKind.OpenBrace] = ("{1, 2}", nameof(ArrayCreationNode)),
            [TokenKind.If] = ("§IF true → 1 §EL → 2 §/I", nameof(ConditionalExpressionNode)),
            [TokenKind.Match] = ("§W value §K _ → 1 §/W", nameof(MatchExpressionNode)),
            [TokenKind.Array] = ("§ARR{a:i32} §/ARR", nameof(ArrayCreationNode)),
            [TokenKind.Index] = ("§IDX values 0", nameof(ArrayAccessNode)),
            [TokenKind.Length] = ("§LEN values", nameof(ArrayLengthNode)),
            [TokenKind.List] = ("§LIST{l:i32} §/LIST", nameof(ListCreationNode)),
            [TokenKind.Dict] = ("§DICT{d:str:i32} §/DICT", nameof(DictionaryCreationNode)),
            [TokenKind.HashSet] = ("§HSET{s:i32} §/HSET", nameof(SetCreationNode)),
            [TokenKind.Has] = ("§HAS{values} 1", nameof(CollectionContainsNode)),
            [TokenKind.Count] = ("§CNT{values}", nameof(CollectionCountNode)),
            [TokenKind.New] = ("§NEW{object} §/NEW", nameof(NewExpressionNode)),
            [TokenKind.AnonymousObject] = ("§ANON Name = \"Ada\" §/ANON", nameof(AnonymousObjectCreationNode)),
            [TokenKind.This] = ("§THIS", nameof(ThisExpressionNode)),
            [TokenKind.Base] = ("§BASE", nameof(BaseExpressionNode)),
            [TokenKind.Call] = ("§C{GetValue} §/C", nameof(CallExpressionNode)),
            [TokenKind.Lambda] = ("§LAM{l:x:i32} x §/LAM", nameof(LambdaExpressionNode)),
            [TokenKind.Await] = ("§AWAIT §C{GetValueAsync} §/C", nameof(AwaitExpressionNode)),
            [TokenKind.Interpolate] = ("§INTERP \"x\" §/INTERP", nameof(InterpolatedStringNode)),
            [TokenKind.NullCoalesce] = ("§?? value 0", nameof(NullCoalesceNode)),
            [TokenKind.NullConditional] = ("§?. value Length", nameof(NullConditionalNode)),
            [TokenKind.RangeOp] = ("§RANGE 1 2", nameof(RangeExpressionNode)),
            [TokenKind.IndexEnd] = ("§^ 1", nameof(IndexFromEndNode)),
            [TokenKind.With] = ("§WITH value §SET{Name} \"Ada\" §/WITH", nameof(WithExpressionNode)),
            [TokenKind.StackAlloc] = ("§SALLOC{i32:4}", nameof(StackAllocNode)),
            [TokenKind.AddressOf] = ("§ADDR value", nameof(AddressOfNode)),
            [TokenKind.Deref] = ("§DEREF pointer", nameof(PointerDereferenceNode)),
            [TokenKind.SizeOf] = ("§SIZEOF{i32}", nameof(SizeOfNode)),
            [TokenKind.Array2D] = ("§ARR2D{i32:2:2}", nameof(MultiDimArrayCreationNode)),
            [TokenKind.Index2D] = ("§IDX2D grid 0 1", nameof(MultiDimArrayAccessNode)),
            [TokenKind.Throw] = ("§TH §NEW{Exception} §/NEW", nameof(ThrowExpressionNode)),
            [TokenKind.RawCSharpExpression] = ("§CS{DateTime.Now}", nameof(RawCSharpExpressionNode)),
            [TokenKind.Hash] = ("#", nameof(SelfRefNode)),
            [TokenKind.At] = ("@value", nameof(ReferenceNode)),
            [TokenKind.Minus] = ("-value", nameof(UnaryOperationNode)),
            [TokenKind.Exclamation] = ("!value", nameof(UnaryOperationNode)),
            [TokenKind.Tilde] = ("~value", nameof(UnaryOperationNode)),
        };

    // Cases above are the original logical subjects and xUnit identities, not admitted syntax.
    // Native equivalents preserve their operands/types; their AST representation can differ.
    private static readonly IReadOnlyDictionary<TokenKind, (string Source, TokenKind Start, string ExpectedType)> NativeCases =
        new Dictionary<TokenKind, (string, TokenKind, string)>
        {
            [TokenKind.IntLiteral] = ("1", TokenKind.IntLiteral, nameof(IntLiteralNode)),
            [TokenKind.StrLiteral] = ("\"x\"", TokenKind.StrLiteral, nameof(StringLiteralNode)),
            [TokenKind.CharLiteral] = ("'x'", TokenKind.CharLiteral, nameof(CharOperationNode)),
            [TokenKind.BoolLiteral] = ("true", TokenKind.BoolLiteral, nameof(BoolLiteralNode)),
            [TokenKind.FloatLiteral] = ("1.5", TokenKind.FloatLiteral, nameof(FloatLiteralNode)),
            [TokenKind.DecimalLiteral] = ("1.5m", TokenKind.DecimalLiteral, nameof(DecimalLiteralNode)),
            [TokenKind.Identifier] = ("value", TokenKind.Identifier, nameof(ReferenceNode)),
            [TokenKind.OpenParen] = ("(1 + 2)", TokenKind.OpenParen, nameof(BinaryOperationNode)),
            [TokenKind.OpenBrace] = ("{1, 2}", TokenKind.OpenBrace, nameof(ArrayCreationNode)),
            [TokenKind.If] = ("if true → 1 else → 2", TokenKind.If, nameof(ConditionalExpressionNode)),
            [TokenKind.Match] = ("match value\n  case _ → 1\n", TokenKind.Match, nameof(MatchExpressionNode)),
            [TokenKind.Array] = ("new int[] {}", TokenKind.New, nameof(ArrayCreationNode)),
            [TokenKind.Index] = ("values[0]", TokenKind.Identifier, nameof(ArrayAccessNode)),
            [TokenKind.Length] = ("values.Length", TokenKind.Identifier, nameof(ReferenceNode)),
            [TokenKind.List] = ("new List<int>()", TokenKind.New, nameof(NewExpressionNode)),
            [TokenKind.Dict] = ("new Dictionary<string,int>()", TokenKind.New, nameof(NewExpressionNode)),
            [TokenKind.HashSet] = ("new HashSet<int>()", TokenKind.New, nameof(NewExpressionNode)),
            [TokenKind.Has] = ("values.Contains(1)", TokenKind.Identifier, nameof(ExpressionCallNode)),
            [TokenKind.Count] = ("values.Count", TokenKind.Identifier, nameof(ReferenceNode)),
            [TokenKind.New] = ("new object()", TokenKind.New, nameof(NewExpressionNode)),
            [TokenKind.AnonymousObject] = ("new { Name = \"Ada\" }", TokenKind.New, nameof(AnonymousObjectCreationNode)),
            [TokenKind.This] = ("this", TokenKind.This, nameof(ThisExpressionNode)),
            [TokenKind.Base] = ("base", TokenKind.Base, nameof(BaseExpressionNode)),
            [TokenKind.Call] = ("GetValue()", TokenKind.Identifier, nameof(CallExpressionNode)),
            [TokenKind.Lambda] = ("(int:x) => x", TokenKind.OpenParen, nameof(LambdaExpressionNode)),
            [TokenKind.Await] = ("await GetValueAsync()", TokenKind.Await, nameof(AwaitExpressionNode)),
            [TokenKind.Interpolate] = ("\"x\"", TokenKind.StrLiteral, nameof(StringLiteralNode)),
            [TokenKind.NullCoalesce] = ("value ?? 0", TokenKind.Identifier, nameof(NullCoalesceNode)),
            [TokenKind.NullConditional] = ("value?.Length", TokenKind.Identifier, nameof(NullConditionalNode)),
            [TokenKind.RangeOp] = ("1..2", TokenKind.IntLiteral, nameof(RangeExpressionNode)),
            [TokenKind.IndexEnd] = ("^1", TokenKind.Caret, nameof(IndexFromEndNode)),
            [TokenKind.With] = ("value with { Name = \"Ada\" }", TokenKind.Identifier, nameof(WithExpressionNode)),
            [TokenKind.Array2D] = ("new int[2,2]", TokenKind.New, nameof(MultiDimArrayCreationNode)),
            [TokenKind.Index2D] = ("grid[0,1]", TokenKind.Identifier, nameof(MultiDimArrayAccessNode)),
            [TokenKind.Throw] = ("throw new Exception()", TokenKind.Throw, nameof(ThrowExpressionNode)),
            [TokenKind.Hash] = ("#", TokenKind.Hash, nameof(SelfRefNode)),
            [TokenKind.At] = ("@value", TokenKind.Identifier, nameof(ReferenceNode)),
            [TokenKind.Minus] = ("-value", TokenKind.Minus, nameof(UnaryOperationNode)),
            [TokenKind.Exclamation] = ("!value", TokenKind.Exclamation, nameof(UnaryOperationNode)),
            [TokenKind.Tilde] = ("~value", TokenKind.Tilde, nameof(UnaryOperationNode)),
        };

    // Explicit token/API fixtures for prefix dispatch that the native equivalents do not
    // exercise. These are NOT source translations or evidence of native acceptance.
    private static readonly IReadOnlyDictionary<TokenKind, (string Tail, TokenKind? End, object? Value)> InternalCases =
        new Dictionary<TokenKind, (string, TokenKind?, object?)>
        {
            [TokenKind.Array] = ("{i32}", TokenKind.EndArray, null),
            [TokenKind.Index] = ("values 0", null, null),
            [TokenKind.Length] = ("values", null, null),
            [TokenKind.List] = ("{i32}", TokenKind.EndList, null),
            [TokenKind.Dict] = ("{str:i32}", TokenKind.EndDict, null),
            [TokenKind.HashSet] = ("{i32}", TokenKind.EndHashSet, null),
            [TokenKind.Has] = ("{values} 1", null, null),
            [TokenKind.Count] = ("{values}", null, null),
            [TokenKind.AnonymousObject] = ("Name = \"Ada\"", TokenKind.EndAnonymousObject, null),
            [TokenKind.Call] = ("{GetValue}", TokenKind.EndCall, null),
            [TokenKind.Lambda] = ("{x:i32} x", TokenKind.EndLambda, null),
            [TokenKind.Interpolate] = ("\"x\"", TokenKind.EndInterpolate, null),
            [TokenKind.NullCoalesce] = ("value 0", null, null),
            [TokenKind.NullConditional] = ("value Length", null, null),
            [TokenKind.RangeOp] = ("2", null, null),
            [TokenKind.IndexEnd] = ("1", null, null),
            [TokenKind.StackAlloc] = ("{i32:4}", null, null),
            [TokenKind.AddressOf] = ("value", null, null),
            [TokenKind.Deref] = ("pointer", null, null),
            [TokenKind.SizeOf] = ("{i32}", null, null),
            [TokenKind.Array2D] = ("{i32:2:2}", null, null),
            [TokenKind.Index2D] = ("grid 0 1", null, null),
            [TokenKind.RawCSharpExpression] = ("", null, "DateTime.Now"),
            [TokenKind.At] = ("value", null, null),
        };

    public static IEnumerable<object[]> RegisteredCases()
    {
        foreach (var (kind, expressionCase) in Cases.OrderBy(pair => pair.Key))
        {
            foreach (var context in Enum.GetValues<ExpressionContext>())
            {
                yield return new object[] { kind, expressionCase.Source, expressionCase.ExpectedType, context };
            }
        }
    }

    [Fact]
    public void CallHeader_DoesNotEatAFollowingBraceInitializerAsAttributes()
    {
        // #911 F6's native equivalent: the brace initializer is an argument, not a header.
        var source = ModuleSource("Sink({1, 2})");
        var module = ParseNative(source);
        var statement = Assert.IsType<ExpressionStatementNode>(Assert.Single(Assert.Single(module.Functions).Body));
        var call = Assert.IsType<CallExpressionNode>(statement.Expression);
        Assert.Equal("Sink", call.Target);
        var argument = Assert.IsType<ArrayCreationNode>(Assert.Single(call.Arguments));
        Assert.Equal(new long[] { 1, 2 }, argument.Initializer.Select(element => Assert.IsType<IntLiteralNode>(element).Value));
        Assert.Equal("Sink", source.Substring(call.CalleeSpan.Start, call.CalleeSpan.Length));
    }

    /// <summary>
    /// Design-doc pin <b>P5</b>. <c>TokenKind.Effects</c> must never become an
    /// expression start. Effect rows are read at six explicit parser insertion points
    /// (design-doc §3.3); every one of them sits next to a place an initializer or a
    /// default value may follow — <c>let Func&lt;int,int&gt;:f ! cw = init</c>,
    /// <c>field Func&lt;int,int&gt;:f ! cw = init</c>, or a callable parameter
    /// <c>Func&lt;int,int&gt;:f ! cw = init</c>. If
    /// <c>Effects</c> were registered, <c>IsExpressionStart()</c> would be true for it
    /// and the initializer parse would swallow the row before the row check ever ran.
    /// </summary>
    [Fact]
    public void EffectsTokenIsNotAnExpressionStart()
    {
        var property = typeof(Parser).GetProperty(
            "RegisteredExpressionStartTokens",
            BindingFlags.Static | BindingFlags.NonPublic);
        var registered = Assert.IsAssignableFrom<IEnumerable<TokenKind>>(property?.GetValue(null)).ToHashSet();

        Assert.DoesNotContain(TokenKind.Effects, registered);
        Assert.DoesNotContain(TokenKind.Effects, Cases.Keys);
        Assert.DoesNotContain(TokenKind.Effects, InternalCases.Keys);
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer("!value", diagnostics).TokenizeAllForParser();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        Assert.Equal(TokenKind.Exclamation, tokens[0].Kind);
        Assert.Contains(TokenKind.Exclamation, registered);
    }

    [Fact]
    public void TestCasesCoverEveryRegisteredExpressionStartToken()
    {
        var property = typeof(Parser).GetProperty(
            "RegisteredExpressionStartTokens",
            BindingFlags.Static | BindingFlags.NonPublic);
        var registered = Assert.IsAssignableFrom<IEnumerable<TokenKind>>(property?.GetValue(null)).ToHashSet();

        Assert.Equal(
            registered.OrderBy(kind => kind),
            NativeCases.Values.Select(expression => expression.Start)
                .Concat(InternalCases.Keys).Distinct().OrderBy(kind => kind));
        Assert.Equal(Cases.Keys.OrderBy(kind => kind),
            NativeCases.Keys.Concat(InternalCases.Keys).Distinct().OrderBy(kind => kind));
        Assert.DoesNotContain(TokenKind.With, registered);
        Assert.DoesNotContain(TokenKind.With, InternalCases.Keys);
        Assert.Equal(TokenKind.Identifier, NativeCases[TokenKind.With].Start);
    }

    [Theory]
    [MemberData(nameof(RegisteredCases))]
    public void RegisteredExpressionStart_ParsesInEveryExpressionContext(
        TokenKind kind,
        string source,
        string expectedType,
        ExpressionContext context)
    {
        Assert.Equal((source, expectedType), Cases[kind]);
        AssertRetiredSourceRejected(source, context);
        if (NativeCases.TryGetValue(kind, out var native))
        {
            var fullSource = ExpressionSource(native.Source, context);
            var diagnostics = new DiagnosticBag();
            var tokens = new Lexer(fullSource, diagnostics).TokenizeAllForParser();
            Assert.False(diagnostics.HasErrors, fullSource + "\n" + string.Join("\n", diagnostics));
            var start = fullSource.IndexOf(native.Source.Split('\n')[0], StringComparison.Ordinal);
            Assert.Equal(native.Start, Assert.Single(tokens.Where(token => token.Span.Start == start)).Kind);
            var module = new Parser(tokens, diagnostics).Parse();
            if (kind == TokenKind.Hash)
            {
                // '#' is lexed natively, but these four ordinary contexts are not predicates.
                var error = Assert.Single(diagnostics);
                Assert.Equal(DiagnosticSeverity.Error, error.Severity);
                Assert.Equal(DiagnosticCode.SelfRefOutsidePredicate, error.Code);
                Assert.Equal("#", fullSource.Substring(error.Span.Start, error.Span.Length));
            }
            else
                Assert.False(diagnostics.HasErrors, fullSource + "\n" + string.Join("\n", diagnostics));
            var expression = ExtractExpression(module, context);
            Assert.Equal(native.ExpectedType, expression.GetType().Name);
            AssertSubject(kind, expression, internalCoverage: false);
        }

        if (InternalCases.ContainsKey(kind))
        {
            var module = ParseWithExpression(GetExpressionTokens(kind, source), context);
            var expression = ExtractExpression(module, context);
            Assert.Equal(expectedType, expression.GetType().Name);
            AssertSubject(kind, expression, internalCoverage: true);
        }
        Assert.True(NativeCases.ContainsKey(kind) || InternalCases.ContainsKey(kind));
    }

    [Theory]
    [InlineData(ExpressionContext.Return)]
    [InlineData(ExpressionContext.Binding)]
    [InlineData(ExpressionContext.CallArgument)]
    [InlineData(ExpressionContext.Nested)]
    public void NativeCaret_ParsesInEveryExpressionContext(ExpressionContext context)
    {
        var source = ExpressionSource("^1", context);
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        Assert.Equal(TokenKind.Caret, Assert.Single(tokens.Where(token => token.Text == "^")).Kind);
        var module = new Parser(tokens, diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        AssertSubject(TokenKind.Caret, ExtractExpression(module, context), internalCoverage: false);
    }

    [Theory]
    [InlineData("(value) ?? 0", "coalesce(value,0)")]
    [InlineData("(@value) ?? 0", "coalesce(@value,0)")]
    [InlineData("(values.Count) ?? 0", "coalesce(values.Count,0)")]
    public void NativeGroupedCoalesce_ParsesAndReparsesInEveryExpressionContext(string source, string expected)
    {
        foreach (var context in Enum.GetValues<ExpressionContext>())
        {
            foreach (var module in ParseAndReparseNative(ExpressionSource(source, context)))
                Assert.Equal(expected, ExpressionShape(ExtractExpression(module, context)));
        }
    }

    [Theory]
    [InlineData("(int)value", "cast<int>(value)")]
    [InlineData("(Widget)value", "cast<Widget>(value)")]
    [InlineData("(int?)value", "cast<int?>(value)")]
    [InlineData("(int)(value ?? 0)", "cast<int>(coalesce(value,0))")]
    [InlineData("(int)(value) ?? 0", "coalesce(cast<int>(value),0)")]
    [InlineData("(int)-value", "cast<int>(Negate(value))")]
    [InlineData("(int)value + 1", "Add(cast<int>(value),1)")]
    [InlineData("(int)(value + 1)", "cast<int>(Add(value,1))")]
    [InlineData("(System.Collections.Generic.List<int>)value", "cast<System.Collections.Generic.List<int>>(value)")]
    public void NativeCast_ParsesAndReparsesWithoutChangingPrecedence(string source, string expected)
    {
        foreach (var context in Enum.GetValues<ExpressionContext>())
        {
            foreach (var module in ParseAndReparseNative(ExpressionSource(source, context)))
                Assert.Equal(expected, ExpressionShape(ExtractExpression(module, context)));
        }
    }

    private static IReadOnlyList<Token> GetExpressionTokens(TokenKind kind, string source)
    {
        Assert.Equal(Cases[kind].Source, source);
        var fixture = InternalCases[kind];
        var tokenFixture = "__prefix__ " + fixture.Tail + (fixture.End.HasValue ? " __end__" : "");
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(tokenFixture, diagnostics)
            .TokenizeAllForParser()
            .Where(token => token.Kind is not TokenKind.Eof and not TokenKind.Dedent)
            .ToList();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return tokens.Select(token => token.Text switch
        {
            "__prefix__" => new Token(kind, token.Text, token.Span, fixture.Value),
            "__end__" => new Token(fixture.End!.Value, token.Text, token.Span),
            _ => token,
        }).ToArray();
    }

    private static ModuleNode ParseWithExpression(
        IReadOnlyList<Token> expressionTokens,
        ExpressionContext context)
    {
        // Only internal dispatch uses token insertion. The native path always lexes full source.
        var markerText = "__expression__".PadRight(Math.Max(14, expressionTokens.Max(token => token.Span.End)), '_');
        var source = ExpressionSource(markerText, context);
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser().ToList();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var markerIndex = tokens.FindIndex(token => token.Text == markerText);
        Assert.True(markerIndex >= 0);
        var marker = tokens[markerIndex];
        tokens.RemoveAt(markerIndex);
        tokens.InsertRange(markerIndex, expressionTokens.Select(token =>
            new Token(token.Kind, token.Text, new TextSpan(
                marker.Span.Start + token.Span.Start, token.Span.Length,
                marker.Span.Line, marker.Span.Column + token.Span.Column - 1), token.Value)));

        var module = new Parser(tokens, diagnostics).Parse();
        Assert.False(diagnostics.HasErrors,
            $"Internal token/API fixture: {expressionTokens[0].Kind}, context: {context}\n{string.Join("\n", diagnostics)}");
        return module;
    }

    private static ExpressionNode ExtractExpression(ModuleNode module, ExpressionContext context)
    {
        Assert.Equal("Test", module.Name);
        var function = Assert.Single(module.Functions);
        Assert.Equal("Test", function.Name);
        var statement = Assert.Single(function.Body);
        switch (context)
        {
            case ExpressionContext.Return:
                return Assert.IsAssignableFrom<ExpressionNode>(Assert.IsType<ReturnStatementNode>(statement).Expression);
            case ExpressionContext.Binding:
                var binding = Assert.IsType<BindStatementNode>(statement);
                Assert.Equal("x", binding.Name);
                return Assert.IsAssignableFrom<ExpressionNode>(binding.Initializer);
            case ExpressionContext.CallArgument:
                var call = Assert.IsType<CallExpressionNode>(Assert.IsType<ExpressionStatementNode>(statement).Expression);
                Assert.Equal("Sink", call.Target);
                return Assert.Single(call.Arguments);
            case ExpressionContext.Nested:
                return ExtractNested(Assert.IsType<ReturnStatementNode>(statement).Expression!);
            default:
                throw new ArgumentOutOfRangeException(nameof(context), context, null);
        }
    }

    private static string ModuleSource(string statement) =>
        "module Test\n  func Test () → object\n    !\n    " + statement.Replace("\n", "\n    ") + "\n";

    private static string ExpressionSource(string expression, ExpressionContext context) => ModuleSource(context switch
    {
        ExpressionContext.Return => "return " + expression,
        ExpressionContext.Binding => "let x = " + expression,
        ExpressionContext.CallArgument => "Sink(" + expression + ")",
        ExpressionContext.Nested => "return (" + expression + ") ?? 0",
        _ => throw new ArgumentOutOfRangeException(nameof(context), context, null),
    });

    private static ExpressionNode ExtractNested(ExpressionNode expression)
    {
        var coalesce = Assert.IsType<NullCoalesceNode>(expression);
        AssertInteger(coalesce.Right, 0);
        return coalesce.Left;
    }

    private static ModuleNode ParseNative(string source)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        Assert.False(diagnostics.HasErrors, source + "\n" + string.Join("\n", diagnostics));
        var module = new Parser(tokens, diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, source + "\n" + string.Join("\n", diagnostics));
        return module;
    }

    private static IEnumerable<ModuleNode> ParseAndReparseNative(string source)
    {
        var module = ParseNative(source);
        yield return module;
        var emitted = new CalorEmitter().Emit(module);
        Assert.DoesNotContain("§", emitted);
        yield return ParseNative(emitted);
    }

    private static string ExpressionShape(ExpressionNode expression) => expression switch
    {
        TypeOperationNode { Operation: TypeOp.Cast } cast =>
            $"cast<{cast.TargetType}>({ExpressionShape(cast.Operand)})",
        NullCoalesceNode coalesce => $"coalesce({ExpressionShape(coalesce.Left)},{ExpressionShape(coalesce.Right)})",
        BinaryOperationNode binary => $"{binary.Operator}({ExpressionShape(binary.Left)},{ExpressionShape(binary.Right)})",
        UnaryOperationNode unary => $"{unary.Operator}({ExpressionShape(unary.Operand)})",
        ReferenceNode reference => (reference.IsVerbatim ? "@" : "") + reference.Name,
        IntLiteralNode integer => integer.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => throw new InvalidOperationException($"Unexpected grouping/cast node: {expression.GetType().Name}"),
    };

    private static void AssertRetiredSourceRejected(string expression, ExpressionContext context)
    {
        var marker = expression.IndexOf('§');
        if (marker < 0 && !expression.StartsWith("DEC:", StringComparison.Ordinal))
            return;

        // The surrounding module is valid: rejection must point at the expression itself.
        ParseNative(ExpressionSource("0", context));
        var source = ExpressionSource(expression, context);
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        var offset = source.IndexOf(expression, StringComparison.Ordinal) + Math.Max(0, marker);
        var code = marker >= 0 ? DiagnosticCode.UnknownSectionMarker : DiagnosticCode.InvalidTypedLiteral;
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == code && diagnostic.Span.Start == offset);
        Assert.Contains(tokens, token => token.Kind == TokenKind.Error && token.Span.Start == offset);
        Assert.DoesNotContain(tokens, token => token.Kind == TokenKind.RawCSharpExpression);
    }

    private static void AssertSubject(TokenKind kind, ExpressionNode expression, bool internalCoverage)
    {
        switch (kind)
        {
            case TokenKind.IntLiteral:
                AssertInteger(expression, 1);
                break;
            case TokenKind.StrLiteral:
                Assert.Equal("x", Assert.IsType<StringLiteralNode>(expression).Value);
                break;
            case TokenKind.CharLiteral:
                var character = Assert.IsType<CharOperationNode>(expression);
                Assert.Equal(CharOp.CharLiteral, character.Operation);
                Assert.Equal("x", Assert.IsType<StringLiteralNode>(Assert.Single(character.Arguments)).Value);
                break;
            case TokenKind.BoolLiteral:
                Assert.True(Assert.IsType<BoolLiteralNode>(expression).Value);
                break;
            case TokenKind.FloatLiteral:
                Assert.Equal(1.5, Assert.IsType<FloatLiteralNode>(expression).Value);
                break;
            case TokenKind.DecimalLiteral:
                Assert.Equal(1.5m, Assert.IsType<DecimalLiteralNode>(expression).Value);
                break;
            case TokenKind.Identifier:
                AssertReference(expression, "value");
                break;
            case TokenKind.OpenParen:
                var binary = Assert.IsType<BinaryOperationNode>(expression);
                Assert.Equal(BinaryOperator.Add, binary.Operator);
                AssertInteger(binary.Left, 1);
                AssertInteger(binary.Right, 2);
                break;
            case TokenKind.OpenBrace:
                Assert.Equal(new long[] { 1, 2 }, Assert.IsType<ArrayCreationNode>(expression).Initializer
                    .Select(element => Assert.IsType<IntLiteralNode>(element).Value));
                break;
            case TokenKind.If:
                var conditional = Assert.IsType<ConditionalExpressionNode>(expression);
                Assert.True(Assert.IsType<BoolLiteralNode>(conditional.Condition).Value);
                AssertInteger(conditional.WhenTrue, 1);
                AssertInteger(conditional.WhenFalse, 2);
                break;
            case TokenKind.Match:
                var match = Assert.IsType<MatchExpressionNode>(expression);
                AssertReference(match.Target, "value");
                var arm = Assert.Single(match.Cases);
                Assert.IsType<WildcardPatternNode>(arm.Pattern);
                AssertInteger(Assert.IsType<ReturnStatementNode>(Assert.Single(arm.Body)).Expression!, 1);
                break;
            case TokenKind.Array:
                var array = Assert.IsType<ArrayCreationNode>(expression);
                Assert.Equal(internalCoverage ? "i32" : "int", array.ElementType);
                Assert.Empty(array.Initializer);
                Assert.Null(array.Size);
                break;
            case TokenKind.Index:
                var index = Assert.IsType<ArrayAccessNode>(expression);
                AssertReference(index.Array, "values");
                AssertInteger(index.Index, 0);
                break;
            case TokenKind.Length:
                if (internalCoverage)
                    AssertReference(Assert.IsType<ArrayLengthNode>(expression).Array, "values");
                else
                    AssertReference(expression, "values.Length");
                break;
            case TokenKind.List:
                if (internalCoverage)
                {
                    var list = Assert.IsType<ListCreationNode>(expression);
                    Assert.Equal("i32", list.ElementType);
                    Assert.Empty(list.Elements);
                }
                else
                    AssertNew(expression, "List<int>");
                break;
            case TokenKind.Dict:
                if (internalCoverage)
                {
                    var dictionary = Assert.IsType<DictionaryCreationNode>(expression);
                    Assert.Equal("str", dictionary.KeyType);
                    Assert.Equal("i32", dictionary.ValueType);
                    Assert.Empty(dictionary.Entries);
                }
                else
                    AssertNew(expression, "Dictionary<string,int>");
                break;
            case TokenKind.HashSet:
                if (internalCoverage)
                {
                    var set = Assert.IsType<SetCreationNode>(expression);
                    Assert.Equal("i32", set.ElementType);
                    Assert.Empty(set.Elements);
                }
                else
                    AssertNew(expression, "HashSet<int>");
                break;
            case TokenKind.Has:
                if (internalCoverage)
                {
                    var contains = Assert.IsType<CollectionContainsNode>(expression);
                    Assert.Equal("values", contains.CollectionName);
                    Assert.Equal(ContainsMode.Value, contains.Mode);
                    AssertInteger(contains.KeyOrValue, 1);
                }
                else
                {
                    var contains = Assert.IsType<ExpressionCallNode>(expression);
                    var target = Assert.IsType<FieldAccessNode>(contains.TargetExpression);
                    AssertReference(target.Target, "values");
                    Assert.Equal("Contains", target.FieldName);
                    AssertInteger(Assert.Single(contains.Arguments), 1);
                }
                break;
            case TokenKind.Count:
                if (internalCoverage)
                    AssertReference(Assert.IsType<CollectionCountNode>(expression).Collection, "values");
                else
                    AssertReference(expression, "values.Count");
                break;
            case TokenKind.New:
                AssertNew(expression, "object");
                break;
            case TokenKind.AnonymousObject:
                var anonymous = Assert.Single(Assert.IsType<AnonymousObjectCreationNode>(expression).Initializers);
                Assert.Equal("Name", anonymous.PropertyName);
                Assert.Equal("Ada", Assert.IsType<StringLiteralNode>(anonymous.Value).Value);
                break;
            case TokenKind.This:
                Assert.IsType<ThisExpressionNode>(expression);
                break;
            case TokenKind.Base:
                Assert.IsType<BaseExpressionNode>(expression);
                break;
            case TokenKind.Call:
                AssertCall(expression, "GetValue");
                break;
            case TokenKind.Lambda:
                var lambda = Assert.IsType<LambdaExpressionNode>(expression);
                var parameter = Assert.Single(lambda.Parameters);
                Assert.Equal("x", parameter.Name);
                Assert.Equal(internalCoverage ? "i32" : "int", parameter.TypeName);
                AssertReference(lambda.ExpressionBody!, "x");
                break;
            case TokenKind.Await:
                AssertCall(Assert.IsType<AwaitExpressionNode>(expression).Awaited, "GetValueAsync");
                break;
            case TokenKind.Interpolate:
                if (internalCoverage)
                    Assert.Equal("x", Assert.IsType<InterpolatedStringTextNode>(
                        Assert.Single(Assert.IsType<InterpolatedStringNode>(expression).Parts)).Text);
                else
                    Assert.Equal("x", Assert.IsType<StringLiteralNode>(expression).Value);
                break;
            case TokenKind.NullCoalesce:
                var coalesce = Assert.IsType<NullCoalesceNode>(expression);
                AssertReference(coalesce.Left, "value");
                AssertInteger(coalesce.Right, 0);
                break;
            case TokenKind.NullConditional:
                var access = Assert.IsType<NullConditionalNode>(expression);
                AssertReference(access.Target, "value");
                Assert.Equal("Length", access.MemberName);
                break;
            case TokenKind.RangeOp:
                var range = Assert.IsType<RangeExpressionNode>(expression);
                // The retained prefix API is open-ended; native 1..2 preserves both operands.
                if (internalCoverage)
                    Assert.Null(range.Start);
                else
                    AssertInteger(range.Start!, 1);
                AssertInteger(range.End!, 2);
                break;
            case TokenKind.IndexEnd:
            case TokenKind.Caret:
                AssertInteger(Assert.IsType<IndexFromEndNode>(expression).Offset, 1);
                break;
            case TokenKind.With:
                var with = Assert.IsType<WithExpressionNode>(expression);
                AssertReference(with.Target, "value");
                var assignment = Assert.Single(with.Assignments);
                Assert.Equal("Name", assignment.PropertyName);
                Assert.Equal("Ada", Assert.IsType<StringLiteralNode>(assignment.Value).Value);
                break;
            case TokenKind.StackAlloc:
                var stack = Assert.IsType<StackAllocNode>(expression);
                Assert.Equal("i32", stack.ElementType);
                AssertInteger(stack.Size!, 4);
                Assert.Empty(stack.Initializer);
                break;
            case TokenKind.AddressOf:
                AssertReference(Assert.IsType<AddressOfNode>(expression).Operand, "value");
                break;
            case TokenKind.Deref:
                AssertReference(Assert.IsType<PointerDereferenceNode>(expression).Operand, "pointer");
                break;
            case TokenKind.SizeOf:
                Assert.Equal("i32", Assert.IsType<SizeOfNode>(expression).TypeName);
                break;
            case TokenKind.Array2D:
                var grid = Assert.IsType<MultiDimArrayCreationNode>(expression);
                Assert.Equal(internalCoverage ? "i32" : "int", grid.ElementType);
                Assert.Equal(2, grid.Rank);
                Assert.Equal(new long[] { 2, 2 }, grid.DimensionSizes.Select(size => Assert.IsType<IntLiteralNode>(size).Value));
                break;
            case TokenKind.Index2D:
                var indices = Assert.IsType<MultiDimArrayAccessNode>(expression);
                AssertReference(indices.Array, "grid");
                Assert.Equal(new long[] { 0, 1 }, indices.Indices.Select(index => Assert.IsType<IntLiteralNode>(index).Value));
                break;
            case TokenKind.Throw:
                AssertNew(Assert.IsType<ThrowExpressionNode>(expression).Exception, "Exception");
                break;
            case TokenKind.RawCSharpExpression:
                Assert.True(internalCoverage);
                Assert.Equal("DateTime.Now", Assert.IsType<RawCSharpExpressionNode>(expression).CSharpCode);
                break;
            case TokenKind.Hash:
                Assert.IsType<SelfRefNode>(expression);
                break;
            case TokenKind.At:
                AssertReference(expression, "value");
                Assert.True(Assert.IsType<ReferenceNode>(expression).IsVerbatim);
                break;
            case TokenKind.Minus:
            case TokenKind.Exclamation:
            case TokenKind.Tilde:
                var unary = Assert.IsType<UnaryOperationNode>(expression);
                AssertReference(unary.Operand, "value");
                Assert.Equal(kind switch
                {
                    TokenKind.Minus => UnaryOperator.Negate,
                    TokenKind.Exclamation => UnaryOperator.Not,
                    _ => UnaryOperator.BitwiseNot,
                }, unary.Operator);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    private static void AssertInteger(ExpressionNode expression, long expected) =>
        Assert.Equal(expected, Assert.IsType<IntLiteralNode>(expression).Value);

    private static void AssertReference(ExpressionNode expression, string expected) =>
        Assert.Equal(expected, Assert.IsType<ReferenceNode>(expression).Name);

    private static void AssertCall(ExpressionNode expression, string target)
    {
        var call = Assert.IsType<CallExpressionNode>(expression);
        Assert.Equal(target, call.Target);
        Assert.Empty(call.Arguments);
    }

    private static void AssertNew(ExpressionNode expression, string type)
    {
        var created = Assert.IsType<NewExpressionNode>(expression);
        Assert.Equal(type, created.TypeName);
        Assert.Empty(created.Arguments);
        Assert.Empty(created.Initializers);
        Assert.Empty(created.CollectionInitializers);
    }
}
