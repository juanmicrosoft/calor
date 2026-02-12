using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class TypeSystemTests
{
    private static List<Token> Tokenize(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        return lexer.TokenizeAll();
    }

    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    #region Lexer Tests

    [Fact]
    public void Lexer_RecognizesSomeKeyword()
    {
        var tokens = Tokenize("§SM", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Some, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void Lexer_RecognizesNoneKeyword()
    {
        var tokens = Tokenize("§NN", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.None, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void Lexer_RecognizesOkKeyword()
    {
        var tokens = Tokenize("§OK", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Ok, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void Lexer_RecognizesErrKeyword()
    {
        var tokens = Tokenize("§ERR", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Err, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void Lexer_RecognizesMatchKeyword()
    {
        var tokens = Tokenize("§W", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Match, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void Lexer_RecognizesEndMatchKeyword()
    {
        var tokens = Tokenize("§/W", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.EndMatch, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void Lexer_RecognizesCaseKeyword()
    {
        var tokens = Tokenize("§K", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Case, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void Lexer_RecognizesRecordKeyword()
    {
        var tokens = Tokenize("§D", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Record, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void Lexer_RecognizesFieldKeyword()
    {
        var tokens = Tokenize("§FL", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Field, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void Lexer_RecognizesTypeKeyword()
    {
        var tokens = Tokenize("§T", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Type, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void Lexer_RecognizesVariantKeyword()
    {
        var tokens = Tokenize("§V", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Variant, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    #endregion

    #region Parser Tests - Option Types

    [Fact]
    public void Parser_ParsesSomeExpression()
    {
        var source = @"
§M{m001:Test}
§F{f001:GetValue:pub}
  §O{?i32}
  §R §SM INT:42
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Single(module.Functions);

        var func = module.Functions[0];
        Assert.Single(func.Body);

        var returnStmt = Assert.IsType<ReturnStatementNode>(func.Body[0]);
        var someExpr = Assert.IsType<SomeExpressionNode>(returnStmt.Expression);
        var intLiteral = Assert.IsType<IntLiteralNode>(someExpr.Value);
        Assert.Equal(42, intLiteral.Value);
    }

    [Fact]
    public void Parser_ParsesNoneExpression()
    {
        var source = @"
§M{m001:Test}
§F{f001:GetNothing:pub}
  §O{?i32}
  §R §NN{i32}
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Single(module.Functions);

        var func = module.Functions[0];
        var returnStmt = Assert.IsType<ReturnStatementNode>(func.Body[0]);
        var noneExpr = Assert.IsType<NoneExpressionNode>(returnStmt.Expression);
        Assert.Equal("INT", noneExpr.TypeName);
    }

    #endregion

    #region Parser Tests - Result Types

    [Fact]
    public void Parser_ParsesOkExpression()
    {
        var source = @"
§M{m001:Test}
§F{f001:GetResult:pub}
  §O{i32!str}
  §R §OK INT:100
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var func = module.Functions[0];
        var returnStmt = Assert.IsType<ReturnStatementNode>(func.Body[0]);
        var okExpr = Assert.IsType<OkExpressionNode>(returnStmt.Expression);
        var intLiteral = Assert.IsType<IntLiteralNode>(okExpr.Value);
        Assert.Equal(100, intLiteral.Value);
    }

    [Fact]
    public void Parser_ParsesErrExpression()
    {
        var source = @"
§M{m001:Test}
§F{f001:GetError:pub}
  §O{str!str}
  §R §ERR STR:""Something went wrong""
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var func = module.Functions[0];
        var returnStmt = Assert.IsType<ReturnStatementNode>(func.Body[0]);
        var errExpr = Assert.IsType<ErrExpressionNode>(returnStmt.Expression);
        var strLiteral = Assert.IsType<StringLiteralNode>(errExpr.Error);
        Assert.Equal("Something went wrong", strLiteral.Value);
    }

    #endregion

    #region Emitter Tests - Option Types

    [Fact]
    public void Emitter_EmitsSomeExpression()
    {
        var someExpr = new SomeExpressionNode(TextSpan.Empty, new IntLiteralNode(TextSpan.Empty, 42));
        var emitter = new CSharpEmitter();
        var result = someExpr.Accept(emitter);

        Assert.Equal("Calor.Runtime.Option.Some(42)", result);
    }

    [Fact]
    public void Emitter_EmitsNoneExpression()
    {
        var noneExpr = new NoneExpressionNode(TextSpan.Empty, "INT");
        var emitter = new CSharpEmitter();
        var result = noneExpr.Accept(emitter);

        Assert.Equal("Calor.Runtime.Option<int>.None()", result);
    }

    [Fact]
    public void Emitter_EmitsNoneExpressionWithoutType()
    {
        var noneExpr = new NoneExpressionNode(TextSpan.Empty, null);
        var emitter = new CSharpEmitter();
        var result = noneExpr.Accept(emitter);

        Assert.Equal("Calor.Runtime.Option.None<object>()", result);
    }

    #endregion

    #region Emitter Tests - Result Types

    [Fact]
    public void Emitter_EmitsOkExpression()
    {
        var okExpr = new OkExpressionNode(TextSpan.Empty, new IntLiteralNode(TextSpan.Empty, 100));
        var emitter = new CSharpEmitter();
        var result = okExpr.Accept(emitter);

        Assert.Equal("Calor.Runtime.Result.Ok<int, string>(100)", result);
    }

    [Fact]
    public void Emitter_EmitsErrExpression()
    {
        var errExpr = new ErrExpressionNode(TextSpan.Empty, new StringLiteralNode(TextSpan.Empty, "error"));
        var emitter = new CSharpEmitter();
        var result = errExpr.Accept(emitter);

        Assert.Equal("Calor.Runtime.Result.Err<object, string>(\"error\")", result);
    }

    #endregion

    #region Emitter Tests - Records

    [Fact]
    public void Emitter_EmitsRecordCreation()
    {
        var recordCreation = new RecordCreationNode(
            TextSpan.Empty,
            "Person",
            new List<FieldAssignmentNode>
            {
                new(TextSpan.Empty, "Name", new StringLiteralNode(TextSpan.Empty, "Alice")),
                new(TextSpan.Empty, "Age", new IntLiteralNode(TextSpan.Empty, 30))
            });

        var emitter = new CSharpEmitter();
        var result = recordCreation.Accept(emitter);

        Assert.Equal("new Person(\"Alice\", 30)", result);
    }

    [Fact]
    public void Emitter_EmitsFieldAccess()
    {
        var fieldAccess = new FieldAccessNode(
            TextSpan.Empty,
            new ReferenceNode(TextSpan.Empty, "person"),
            "Name");

        var emitter = new CSharpEmitter();
        var result = fieldAccess.Accept(emitter);

        Assert.Equal("person.Name", result);
    }

    #endregion

    #region Runtime Tests - Option

    [Fact]
    public void Option_SomeContainsValue()
    {
        var option = Calor.Runtime.Option<int>.Some(42);

        Assert.True(option.IsSome);
        Assert.False(option.IsNone);
        Assert.Equal(42, option.Unwrap());
    }

    [Fact]
    public void Option_NoneIsEmpty()
    {
        var option = Calor.Runtime.Option<int>.None();

        Assert.False(option.IsSome);
        Assert.True(option.IsNone);
    }

    [Fact]
    public void Option_UnwrapOnNoneThrows()
    {
        var option = Calor.Runtime.Option<int>.None();

        Assert.Throws<InvalidOperationException>(() => option.Unwrap());
    }

    [Fact]
    public void Option_UnwrapOrReturnsDefaultForNone()
    {
        var option = Calor.Runtime.Option<int>.None();

        Assert.Equal(99, option.UnwrapOr(99));
    }

    [Fact]
    public void Option_MapTransformsValue()
    {
        var option = Calor.Runtime.Option<int>.Some(10);
        var mapped = option.Map(x => x * 2);

        Assert.True(mapped.IsSome);
        Assert.Equal(20, mapped.Unwrap());
    }

    [Fact]
    public void Option_MapOnNoneReturnsNone()
    {
        var option = Calor.Runtime.Option<int>.None();
        var mapped = option.Map(x => x * 2);

        Assert.True(mapped.IsNone);
    }

    [Fact]
    public void Option_MatchCallsCorrectBranch()
    {
        var some = Calor.Runtime.Option<int>.Some(5);
        var none = Calor.Runtime.Option<int>.None();

        var someResult = some.Match(v => $"Got {v}", () => "Nothing");
        var noneResult = none.Match(v => $"Got {v}", () => "Nothing");

        Assert.Equal("Got 5", someResult);
        Assert.Equal("Nothing", noneResult);
    }

    [Fact]
    public void Option_AndThenChains()
    {
        var option = Calor.Runtime.Option<int>.Some(10);
        var chained = option.AndThen(x => x > 5
            ? Calor.Runtime.Option<string>.Some($"Value is {x}")
            : Calor.Runtime.Option<string>.None());

        Assert.True(chained.IsSome);
        Assert.Equal("Value is 10", chained.Unwrap());
    }

    [Fact]
    public void Option_FilterKeepsMatchingValues()
    {
        var option = Calor.Runtime.Option<int>.Some(10);
        var filtered = option.Filter(x => x > 5);

        Assert.True(filtered.IsSome);
        Assert.Equal(10, filtered.Unwrap());
    }

    [Fact]
    public void Option_FilterRemovesNonMatchingValues()
    {
        var option = Calor.Runtime.Option<int>.Some(3);
        var filtered = option.Filter(x => x > 5);

        Assert.True(filtered.IsNone);
    }

    [Fact]
    public void Option_OkOrConvertsToResult()
    {
        var some = Calor.Runtime.Option<int>.Some(42);
        var none = Calor.Runtime.Option<int>.None();

        var okResult = some.OkOr("error");
        var errResult = none.OkOr("error");

        Assert.True(okResult.IsOk);
        Assert.Equal(42, okResult.Unwrap());
        Assert.True(errResult.IsErr);
        Assert.Equal("error", errResult.UnwrapErr());
    }

    [Fact]
    public void Option_EqualsComparesValues()
    {
        var a = Calor.Runtime.Option<int>.Some(42);
        var b = Calor.Runtime.Option<int>.Some(42);
        var c = Calor.Runtime.Option<int>.Some(99);
        var none1 = Calor.Runtime.Option<int>.None();
        var none2 = Calor.Runtime.Option<int>.None();

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(none1, none2);
        Assert.NotEqual(a, none1);
    }

    #endregion

    #region Runtime Tests - Result

    [Fact]
    public void Result_OkContainsValue()
    {
        var result = Calor.Runtime.Result<int, string>.Ok(42);

        Assert.True(result.IsOk);
        Assert.False(result.IsErr);
        Assert.Equal(42, result.Unwrap());
    }

    [Fact]
    public void Result_ErrContainsError()
    {
        var result = Calor.Runtime.Result<int, string>.Err("failed");

        Assert.False(result.IsOk);
        Assert.True(result.IsErr);
        Assert.Equal("failed", result.UnwrapErr());
    }

    [Fact]
    public void Result_UnwrapOnErrThrows()
    {
        var result = Calor.Runtime.Result<int, string>.Err("failed");

        Assert.Throws<InvalidOperationException>(() => result.Unwrap());
    }

    [Fact]
    public void Result_UnwrapErrOnOkThrows()
    {
        var result = Calor.Runtime.Result<int, string>.Ok(42);

        Assert.Throws<InvalidOperationException>(() => result.UnwrapErr());
    }

    [Fact]
    public void Result_UnwrapOrReturnsDefaultForErr()
    {
        var result = Calor.Runtime.Result<int, string>.Err("failed");

        Assert.Equal(99, result.UnwrapOr(99));
    }

    [Fact]
    public void Result_MapTransformsOkValue()
    {
        var result = Calor.Runtime.Result<int, string>.Ok(10);
        var mapped = result.Map(x => x * 2);

        Assert.True(mapped.IsOk);
        Assert.Equal(20, mapped.Unwrap());
    }

    [Fact]
    public void Result_MapOnErrReturnsErr()
    {
        var result = Calor.Runtime.Result<int, string>.Err("failed");
        var mapped = result.Map(x => x * 2);

        Assert.True(mapped.IsErr);
        Assert.Equal("failed", mapped.UnwrapErr());
    }

    [Fact]
    public void Result_MapErrTransformsError()
    {
        var result = Calor.Runtime.Result<int, string>.Err("error");
        var mapped = result.MapErr(e => e.ToUpper());

        Assert.True(mapped.IsErr);
        Assert.Equal("ERROR", mapped.UnwrapErr());
    }

    [Fact]
    public void Result_MatchCallsCorrectBranch()
    {
        var ok = Calor.Runtime.Result<int, string>.Ok(5);
        var err = Calor.Runtime.Result<int, string>.Err("failed");

        var okResult = ok.Match(v => $"Got {v}", e => $"Error: {e}");
        var errResult = err.Match(v => $"Got {v}", e => $"Error: {e}");

        Assert.Equal("Got 5", okResult);
        Assert.Equal("Error: failed", errResult);
    }

    [Fact]
    public void Result_AndThenChains()
    {
        var result = Calor.Runtime.Result<int, string>.Ok(10);
        var chained = result.AndThen(x => x > 5
            ? Calor.Runtime.Result<string, string>.Ok($"Value is {x}")
            : Calor.Runtime.Result<string, string>.Err("Too small"));

        Assert.True(chained.IsOk);
        Assert.Equal("Value is 10", chained.Unwrap());
    }

    [Fact]
    public void Result_ToOptionConvertsOkToSome()
    {
        var ok = Calor.Runtime.Result<int, string>.Ok(42);
        var err = Calor.Runtime.Result<int, string>.Err("failed");

        var okOption = ok.ToOption();
        var errOption = err.ToOption();

        Assert.True(okOption.IsSome);
        Assert.Equal(42, okOption.Unwrap());
        Assert.True(errOption.IsNone);
    }

    [Fact]
    public void Result_EqualsComparesValues()
    {
        var a = Calor.Runtime.Result<int, string>.Ok(42);
        var b = Calor.Runtime.Result<int, string>.Ok(42);
        var c = Calor.Runtime.Result<int, string>.Ok(99);
        var err1 = Calor.Runtime.Result<int, string>.Err("x");
        var err2 = Calor.Runtime.Result<int, string>.Err("x");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(err1, err2);
        Assert.NotEqual(a, err1);
    }

    #endregion

    #region Helper Static Methods

    [Fact]
    public void Option_StaticSomeCreatesOption()
    {
        var option = Calor.Runtime.Option.Some(42);

        Assert.True(option.IsSome);
        Assert.Equal(42, option.Unwrap());
    }

    [Fact]
    public void Option_StaticNoneCreatesEmpty()
    {
        var option = Calor.Runtime.Option.None<int>();

        Assert.True(option.IsNone);
    }

    [Fact]
    public void Option_FromNullableConvertsNullableStruct()
    {
        int? hasValue = 42;
        int? noValue = null;

        var some = Calor.Runtime.Option.FromNullable(hasValue);
        var none = Calor.Runtime.Option.FromNullable(noValue);

        Assert.True(some.IsSome);
        Assert.Equal(42, some.Unwrap());
        Assert.True(none.IsNone);
    }

    [Fact]
    public void Option_FromNullableConvertsNullableRef()
    {
        string? hasValue = "hello";
        string? noValue = null;

        var some = Calor.Runtime.Option.FromNullable(hasValue);
        var none = Calor.Runtime.Option.FromNullable(noValue);

        Assert.True(some.IsSome);
        Assert.Equal("hello", some.Unwrap());
        Assert.True(none.IsNone);
    }

    [Fact]
    public void Result_StaticOkCreatesResult()
    {
        var result = Calor.Runtime.Result.Ok<int, string>(42);

        Assert.True(result.IsOk);
        Assert.Equal(42, result.Unwrap());
    }

    [Fact]
    public void Result_StaticErrCreatesResult()
    {
        var result = Calor.Runtime.Result.Err<int, string>("failed");

        Assert.True(result.IsErr);
        Assert.Equal("failed", result.UnwrapErr());
    }

    #endregion

    #region TypeMapper - Expanded Format Tests

    [Theory]
    [InlineData("OPTION[inner=INT]", "int?")]
    [InlineData("OPTION[inner=STRING]", "string?")]
    [InlineData("OPTION[inner=BOOL]", "bool?")]
    [InlineData("OPTION[inner=FLOAT]", "double?")]
    public void CalorToCSharp_ExpandedOptionFormat_MapsCorrectly(string input, string expected)
    {
        var result = Migration.TypeMapper.CalorToCSharp(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CalorToCSharp_NestedOptionFormat_MapsCorrectly()
    {
        // OPTION[inner=OPTION[inner=INT]] -> int??
        var result = Migration.TypeMapper.CalorToCSharp("OPTION[inner=OPTION[inner=INT]]");
        Assert.Equal("int??", result);
    }

    [Theory]
    [InlineData("INT[bits=8][signed=true]", "sbyte")]
    [InlineData("INT[bits=8][signed=false]", "byte")]
    [InlineData("INT[bits=16][signed=true]", "short")]
    [InlineData("INT[bits=16][signed=false]", "ushort")]
    [InlineData("INT[bits=32][signed=false]", "uint")]
    [InlineData("INT[bits=64][signed=true]", "long")]
    [InlineData("INT[bits=64][signed=false]", "ulong")]
    public void CalorToCSharp_ExpandedIntFormat_MapsCorrectly(string input, string expected)
    {
        var result = Migration.TypeMapper.CalorToCSharp(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("FLOAT[bits=32]", "float")]
    [InlineData("FLOAT[bits=64]", "double")]
    public void CalorToCSharp_ExpandedFloatFormat_MapsCorrectly(string input, string expected)
    {
        var result = Migration.TypeMapper.CalorToCSharp(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CalorToCSharp_ExpandedResultFormat_MapsCorrectly()
    {
        var result = Migration.TypeMapper.CalorToCSharp("RESULT[ok=INT][err=STRING]");
        Assert.Equal("Calor.Runtime.Result<int, string>", result);
    }

    [Fact]
    public void CalorToCSharp_ExpandedResultWithComplexTypes_MapsCorrectly()
    {
        var result = Migration.TypeMapper.CalorToCSharp("RESULT[ok=OPTION[inner=INT]][err=STRING]");
        Assert.Equal("Calor.Runtime.Result<int?, string>", result);
    }

    [Fact]
    public void CalorToCSharp_OptionWithExpandedIntInner_MapsCorrectly()
    {
        // ?i64 expands to OPTION[inner=INT[bits=64][signed=true]]
        var result = Migration.TypeMapper.CalorToCSharp("OPTION[inner=INT[bits=64][signed=true]]");
        Assert.Equal("long?", result);
    }

    #endregion

    #region End-to-End: Option/Result Return Types

    [Fact]
    public void Emitter_OptionReturnType_EmitsNullable()
    {
        var source = """
            §M{m001:Test}
            §F{f001:FindValue:pub}
              §I{i32:id}
              §O{?i32}
              §R §SM INT:42
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, FormatDiagnostics(diagnostics));

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(module);

        Assert.Contains("int?", csharp);
        Assert.DoesNotContain("OPTION", csharp);
    }

    [Fact]
    public void Emitter_OptionStringReturnType_EmitsNullable()
    {
        var source = """
            §M{m001:Test}
            §F{f001:FindName:pub}
              §O{?str}
              §R §SM STR:"hello"
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, FormatDiagnostics(diagnostics));

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(module);

        Assert.Contains("string?", csharp);
        Assert.DoesNotContain("OPTION", csharp);
    }

    [Fact]
    public void Emitter_OptionParameter_EmitsNullable()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Process:pub}
              §I{?i32:value}
              §O{void}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, FormatDiagnostics(diagnostics));

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(module);

        Assert.Contains("int? value", csharp);
        Assert.DoesNotContain("OPTION", csharp);
    }

    [Fact]
    public void Emitter_ResultReturnType_EmitsResultGeneric()
    {
        var source = """
            §M{m001:Test}
            §F{f001:TryParse:pub}
              §I{str:input}
              §O{i32!str}
              §R §OK INT:42
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, FormatDiagnostics(diagnostics));

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(module);

        Assert.Contains("Calor.Runtime.Result<int, string>", csharp);
        Assert.DoesNotContain("RESULT[", csharp);
    }

    [Fact]
    public void Emitter_SizedIntReturnType_EmitsCorrectType()
    {
        var source = """
            §M{m001:Test}
            §F{f001:GetLong:pub}
              §O{i64}
              §R INT:100
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, FormatDiagnostics(diagnostics));

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(module);

        Assert.Contains("long", csharp);
        Assert.DoesNotContain("INT[", csharp);
    }

    [Fact]
    public void Emitter_SizedFloatReturnType_EmitsFloat()
    {
        var source = """
            §M{m001:Test}
            §F{f001:GetFloat:pub}
              §O{f32}
              §R FLOAT:1.5
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, FormatDiagnostics(diagnostics));

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(module);

        Assert.Contains("float", csharp);
        Assert.DoesNotContain("FLOAT[", csharp);
    }

    private static string FormatDiagnostics(DiagnosticBag diagnostics)
    {
        return string.Join("\n", diagnostics.Select(d => $"{d.Code}: {d.Message}"));
    }

    #endregion
}
