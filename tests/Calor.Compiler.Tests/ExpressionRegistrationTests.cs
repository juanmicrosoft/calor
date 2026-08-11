using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
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
            [TokenKind.BoolLiteral] = ("true", nameof(BoolLiteralNode)),
            [TokenKind.FloatLiteral] = ("1.5", nameof(FloatLiteralNode)),
            [TokenKind.DecimalLiteral] = ("DEC:1.5", nameof(DecimalLiteralNode)),
            [TokenKind.Identifier] = ("value", nameof(ReferenceNode)),
            [TokenKind.OpenParen] = ("(+ 1 2)", nameof(BinaryOperationNode)),
            [TokenKind.OpenBrace] = ("{1, 2}", nameof(ArrayCreationNode)),
            [TokenKind.If] = ("§IF{i} true → 1 §EL → 2 §/I{i}", nameof(ConditionalExpressionNode)),
            [TokenKind.Some] = ("§SM 1", nameof(SomeExpressionNode)),
            [TokenKind.None] = ("§NN{i32}", nameof(NoneExpressionNode)),
            [TokenKind.Ok] = ("§OK 1", nameof(OkExpressionNode)),
            [TokenKind.Err] = ("§ERR \"error\"", nameof(ErrExpressionNode)),
            [TokenKind.Match] = ("§W{w} value §K _ → 1 §/W{w}", nameof(MatchExpressionNode)),
            [TokenKind.Record] = ("§D{Person} §FL{Name} \"Ada\"", nameof(RecordCreationNode)),
            [TokenKind.Array] = ("§ARR{a:i32} §/ARR{a}", nameof(ArrayCreationNode)),
            [TokenKind.Index] = ("§IDX values 0", nameof(ArrayAccessNode)),
            [TokenKind.Length] = ("§LEN values", nameof(ArrayLengthNode)),
            [TokenKind.List] = ("§LIST{l:i32} §/LIST{l}", nameof(ListCreationNode)),
            [TokenKind.Dict] = ("§DICT{d:str:i32} §/DICT{d}", nameof(DictionaryCreationNode)),
            [TokenKind.HashSet] = ("§HSET{s:i32} §/HSET{s}", nameof(SetCreationNode)),
            [TokenKind.Has] = ("§HAS{values} 1", nameof(CollectionContainsNode)),
            [TokenKind.Count] = ("§CNT{values}", nameof(CollectionCountNode)),
            [TokenKind.Generic] = ("synthetic", nameof(GenericTypeNode)),
            [TokenKind.New] = ("§NEW{object} §/NEW", nameof(NewExpressionNode)),
            [TokenKind.AnonymousObject] = ("§ANON Name = \"Ada\" §/ANON", nameof(AnonymousObjectCreationNode)),
            [TokenKind.This] = ("§THIS", nameof(ThisExpressionNode)),
            [TokenKind.Base] = ("§BASE", nameof(BaseExpressionNode)),
            [TokenKind.Call] = ("§C{GetValue} §/C", nameof(CallExpressionNode)),
            [TokenKind.Lambda] = ("§LAM{l:x:i32} x §/LAM{l}", nameof(LambdaExpressionNode)),
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
            [TokenKind.Array2D] = ("§ARR2D{a:grid:i32:2:2}", nameof(MultiDimArrayCreationNode)),
            [TokenKind.Index2D] = ("§IDX2D grid 0 1", nameof(MultiDimArrayAccessNode)),
            [TokenKind.Throw] = ("§TH §NEW{Exception} §/NEW", nameof(ThrowExpressionNode)),
            [TokenKind.RawCSharpExpression] = ("§CS{DateTime.Now}", nameof(RawCSharpExpressionNode)),
            [TokenKind.Hash] = ("#", nameof(SelfRefNode)),
            [TokenKind.At] = ("@value", nameof(ReferenceNode)),
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
        // #911 review F6: pre-fix, `§C{Sink} {1, 2} §/C` SILENTLY merged the brace
        // group into the call header — Target became "Sink" + "1, 2" garbage with zero
        // diagnostics. With maxGroups: 1 on the §C header, the brace group parses as a
        // collection-initializer argument. (§B got the same fix with the OpenBrace
        // unification; §W and §LAM headers are covered by the same one-group rule.)
        const string source = """
            §M{m:Test}
              §F{f:Test:pub}
                §O{object}
                §C{Sink} {1, 2} §/C
            """;
        var diagnostics = new Calor.Compiler.Diagnostics.DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser().ToList();
        var module = new Parser(tokens, diagnostics).Parse();
        var call = Assert.IsType<CallStatementNode>(Assert.Single(Assert.Single(module.Functions).Body));
        Assert.Equal("Sink", call.Target);
        Assert.Single(call.Arguments);
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
            Cases.Keys.OrderBy(kind => kind));
    }

    [Theory]
    [MemberData(nameof(RegisteredCases))]
    public void RegisteredExpressionStart_ParsesInEveryExpressionContext(
        TokenKind kind,
        string source,
        string expectedType,
        ExpressionContext context)
    {
        var expressionTokens = GetExpressionTokens(kind, source);
        var module = ParseWithExpression(expressionTokens, context);
        var expression = ExtractExpression(module, context);

        Assert.Equal(expectedType, expression.GetType().Name);
    }

    private static IReadOnlyList<Token> GetExpressionTokens(TokenKind kind, string source)
    {
        if (kind == TokenKind.Generic)
        {
            return
            [
                new Token(TokenKind.Generic, "§G", TextSpan.Empty),
                new Token(TokenKind.OpenBrace, "{", TextSpan.Empty),
                new Token(TokenKind.Identifier, "List", TextSpan.Empty),
                new Token(TokenKind.Colon, ":", TextSpan.Empty),
                new Token(TokenKind.Identifier, "i32", TextSpan.Empty),
                new Token(TokenKind.CloseBrace, "}", TextSpan.Empty),
            ];
        }

        var diagnostics = new DiagnosticBag();
        return new Lexer(source, diagnostics)
            .TokenizeAllForParser()
            .Where(token => token.Kind is not TokenKind.Eof and not TokenKind.Dedent)
            .ToList();
    }

    private static ModuleNode ParseWithExpression(
        IReadOnlyList<Token> expressionTokens,
        ExpressionContext context)
    {
        var statement = context switch
        {
            ExpressionContext.Return => "§R __expression__",
            ExpressionContext.Binding => "§B{x} __expression__",
            ExpressionContext.CallArgument => "§C{Sink} §A __expression__ §/C",
            ExpressionContext.Nested => "§R §SM __expression__",
            _ => throw new ArgumentOutOfRangeException(nameof(context), context, null),
        };
        var source = $$"""
            §M{m:Test}
              §F{f:Test:pub}
                §O{object}
                {{statement}}
            """;

        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser().ToList();
        var marker = tokens.FindIndex(token =>
            token.Kind == TokenKind.Identifier && token.Text == "__expression__");
        Assert.True(marker >= 0);
        tokens.RemoveAt(marker);
        tokens.InsertRange(marker, expressionTokens);

        return new Parser(tokens, diagnostics).Parse();
    }

    private static ExpressionNode ExtractExpression(ModuleNode module, ExpressionContext context)
    {
        var function = Assert.Single(module.Functions);
        return context switch
        {
            ExpressionContext.Return => Assert.IsType<ReturnStatementNode>(Assert.Single(function.Body)).Expression!,
            ExpressionContext.Binding => Assert.IsType<BindStatementNode>(Assert.Single(function.Body)).Initializer!,
            ExpressionContext.CallArgument => Assert.Single(
                Assert.IsType<CallStatementNode>(Assert.Single(function.Body)).Arguments),
            ExpressionContext.Nested => Assert.IsType<SomeExpressionNode>(
                Assert.IsType<ReturnStatementNode>(Assert.Single(function.Body)).Expression).Value,
            _ => throw new ArgumentOutOfRangeException(nameof(context), context, null),
        };
    }
}
