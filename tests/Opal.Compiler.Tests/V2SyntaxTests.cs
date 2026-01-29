using Opal.Compiler.Ast;
using Opal.Compiler.Diagnostics;
using Opal.Compiler.Parsing;
using Xunit;

namespace Opal.Compiler.Tests;

public class V2SyntaxTests
{
    private static List<Token> Tokenize(string source)
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        return lexer.TokenizeAll();
    }

    #region Single-Letter Keywords

    [Fact]
    public void Lexer_RecognizesSingleLetterModule()
    {
        var tokens = Tokenize("§M");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Module, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void Lexer_RecognizesSingleLetterFunc()
    {
        var tokens = Tokenize("§F");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Func, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesSingleLetterCall()
    {
        var tokens = Tokenize("§C");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Call, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesSingleLetterBind()
    {
        var tokens = Tokenize("§B");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Bind, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesSingleLetterReturn()
    {
        var tokens = Tokenize("§R");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Return, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesSingleLetterInput()
    {
        var tokens = Tokenize("§I");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.In, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesSingleLetterOutput()
    {
        var tokens = Tokenize("§O");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Out, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesSingleLetterArg()
    {
        var tokens = Tokenize("§A");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Arg, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesSingleLetterEffects()
    {
        var tokens = Tokenize("§E");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Effects, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesSingleLetterLoop()
    {
        var tokens = Tokenize("§L");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.For, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesSingleLetterMatch()
    {
        var tokens = Tokenize("§W");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Match, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesSingleLetterCase()
    {
        var tokens = Tokenize("§K");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Case, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesSingleLetterRequires()
    {
        var tokens = Tokenize("§Q");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Requires, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesSingleLetterEnsures()
    {
        var tokens = Tokenize("§S");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Ensures, tokens[0].Kind);
    }

    #endregion

    #region Closing Tags

    [Fact]
    public void Lexer_RecognizesClosingModule()
    {
        var tokens = Tokenize("§/M");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.EndModule, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesClosingFunc()
    {
        var tokens = Tokenize("§/F");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.EndFunc, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesClosingCall()
    {
        var tokens = Tokenize("§/C");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.EndCall, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesClosingIf()
    {
        var tokens = Tokenize("§/I");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.EndIf, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesClosingLoop()
    {
        var tokens = Tokenize("§/L");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.EndFor, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesClosingMatch()
    {
        var tokens = Tokenize("§/W");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.EndMatch, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesDoKeyword()
    {
        var tokens = Tokenize("§DO");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Do, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesEndDoKeyword()
    {
        var tokens = Tokenize("§/DO");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.EndDo, tokens[0].Kind);
    }

    #endregion

    #region Mixed V1 and V2

    [Fact]
    public void Lexer_HandlesMixedV1AndV2Keywords()
    {
        var tokens = Tokenize("§M §MODULE §F §FUNC");

        Assert.Equal(5, tokens.Count);
        Assert.Equal(TokenKind.Module, tokens[0].Kind);
        Assert.Equal(TokenKind.Module, tokens[1].Kind);
        Assert.Equal(TokenKind.Func, tokens[2].Kind);
        Assert.Equal(TokenKind.Func, tokens[3].Kind);
    }

    [Fact]
    public void Lexer_HandlesMixedClosingTags()
    {
        var tokens = Tokenize("§/F §END_FUNC");

        Assert.Equal(3, tokens.Count);
        Assert.Equal(TokenKind.EndFunc, tokens[0].Kind);
        Assert.Equal(TokenKind.EndFunc, tokens[1].Kind);
    }

    #endregion

    #region V2 Full Program

    [Fact]
    public void Lexer_TokenizesV2Program()
    {
        var source = @"
§M[m001:Hello]
§F[f001:Main:pub]
  §O[void]
  §E[cw]
  §C[Console.WriteLine]
    §A ""Hello from OPAL v2!""
  §/C
§/F[f001]
§/M[m001]
";
        var tokens = Tokenize(source);

        // Verify key tokens are present
        Assert.Contains(tokens, t => t.Kind == TokenKind.Module);
        Assert.Contains(tokens, t => t.Kind == TokenKind.Func);
        Assert.Contains(tokens, t => t.Kind == TokenKind.Out);
        Assert.Contains(tokens, t => t.Kind == TokenKind.Effects);
        Assert.Contains(tokens, t => t.Kind == TokenKind.Call);
        Assert.Contains(tokens, t => t.Kind == TokenKind.Arg);
        Assert.Contains(tokens, t => t.Kind == TokenKind.EndCall);
        Assert.Contains(tokens, t => t.Kind == TokenKind.EndFunc);
        Assert.Contains(tokens, t => t.Kind == TokenKind.EndModule);
    }

    #endregion

    #region V2 Tokens

    [Fact]
    public void Lexer_RecognizesColon()
    {
        var tokens = Tokenize(":");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Colon, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesExclamation()
    {
        var tokens = Tokenize("!");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Exclamation, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesTilde()
    {
        var tokens = Tokenize("~");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Tilde, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesHash()
    {
        var tokens = Tokenize("#");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Hash, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesQuestion()
    {
        var tokens = Tokenize("?");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Question, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_TokenizesPositionalAttribute()
    {
        var tokens = Tokenize("[f001:Main:pub]");

        // Should be: [ f001 : Main : pub ]
        Assert.Equal(TokenKind.OpenBracket, tokens[0].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[1].Kind);
        Assert.Equal("f001", tokens[1].Text);
        Assert.Equal(TokenKind.Colon, tokens[2].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[3].Kind);
        Assert.Equal("Main", tokens[3].Text);
        Assert.Equal(TokenKind.Colon, tokens[4].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[5].Kind);
        Assert.Equal("pub", tokens[5].Text);
        Assert.Equal(TokenKind.CloseBracket, tokens[6].Kind);
    }

    [Fact]
    public void Lexer_TokenizesFallibleCall()
    {
        var tokens = Tokenize("[RiskyOp!]");

        Assert.Equal(TokenKind.OpenBracket, tokens[0].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[1].Kind);
        Assert.Equal("RiskyOp", tokens[1].Text);
        Assert.Equal(TokenKind.Exclamation, tokens[2].Kind);
        Assert.Equal(TokenKind.CloseBracket, tokens[3].Kind);
    }

    [Fact]
    public void Lexer_TokenizesMutableBind()
    {
        var tokens = Tokenize("[~myVar]");

        Assert.Equal(TokenKind.OpenBracket, tokens[0].Kind);
        Assert.Equal(TokenKind.Tilde, tokens[1].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[2].Kind);
        Assert.Equal("myVar", tokens[2].Text);
        Assert.Equal(TokenKind.CloseBracket, tokens[3].Kind);
    }

    [Fact]
    public void Lexer_TokenizesOptionType()
    {
        var tokens = Tokenize("[?i32]");

        Assert.Equal(TokenKind.OpenBracket, tokens[0].Kind);
        Assert.Equal(TokenKind.Question, tokens[1].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[2].Kind);
        Assert.Equal("i32", tokens[2].Text);
        Assert.Equal(TokenKind.CloseBracket, tokens[3].Kind);
    }

    [Fact]
    public void Lexer_TokenizesResultType()
    {
        var tokens = Tokenize("[i32!str]");

        Assert.Equal(TokenKind.OpenBracket, tokens[0].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[1].Kind);
        Assert.Equal("i32", tokens[1].Text);
        Assert.Equal(TokenKind.Exclamation, tokens[2].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[3].Kind);
        Assert.Equal("str", tokens[3].Text);
        Assert.Equal(TokenKind.CloseBracket, tokens[4].Kind);
    }

    #endregion

    #region V2 Parser Integration

    [Fact]
    public void Parser_ParsesV2ModuleWithPositionalAttributes()
    {
        var diagnostics = new DiagnosticBag();
        var source = "§M[m001:Hello] §/M[m001]";
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);

        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Equal("m001", module.Id);
        Assert.Equal("Hello", module.Name);
    }

    [Fact]
    public void Parser_ParsesV2FunctionWithVisibility()
    {
        var diagnostics = new DiagnosticBag();
        var source = @"
§M[m001:Test]
§F[f001:MyFunc:pub]
  §O[void]
  §BODY
  §END_BODY
§/F[f001]
§/M[m001]";
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);

        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Single(module.Functions);
        Assert.Equal("MyFunc", module.Functions[0].Name);
        Assert.Equal(Ast.Visibility.Public, module.Functions[0].Visibility);
    }

    [Fact]
    public void Parser_ParsesV2OutputType()
    {
        var diagnostics = new DiagnosticBag();
        var source = @"
§M[m001:Test]
§F[f001:Add:pub]
  §O[i32]
  §BODY
    §RETURN INT:0
  §END_BODY
§/F[f001]
§/M[m001]";
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);

        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Equal("INT", module.Functions[0].Output?.TypeName);
    }

    [Fact]
    public void Parser_ParsesV2InputParameters()
    {
        var diagnostics = new DiagnosticBag();
        var source = @"
§M[m001:Test]
§F[f001:Add:pub]
  §I[i32:a]
  §I[i32:b]
  §O[i32]
  §BODY
    §RETURN INT:0
  §END_BODY
§/F[f001]
§/M[m001]";
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);

        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Equal(2, module.Functions[0].Parameters.Count);
        Assert.Equal("a", module.Functions[0].Parameters[0].Name);
        Assert.Equal("INT", module.Functions[0].Parameters[0].TypeName);
    }

    [Fact]
    public void Parser_ParsesV2EffectShortcodes()
    {
        var diagnostics = new DiagnosticBag();
        var source = @"
§M[m001:Test]
§F[f001:Print:pub]
  §O[void]
  §E[cw]
  §BODY
  §END_BODY
§/F[f001]
§/M[m001]";
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);

        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.NotNull(module.Functions[0].Effects);
        Assert.Equal("console_write", module.Functions[0].Effects?.Effects["io"]);
    }

    [Fact]
    public void Parser_ParsesV2ImplicitBody()
    {
        var diagnostics = new DiagnosticBag();
        // No §BODY/§END_BODY markers - implicit body
        var source = @"
§M[m001:Test]
§F[f001:Print:pub]
  §O[void]
  §E[cw]
  §C[Console.WriteLine]
    §A ""Hello v2!""
  §/C
§/F[f001]
§/M[m001]";
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);

        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Single(module.Functions);
        Assert.Single(module.Functions[0].Body);
    }

    [Fact]
    public void Parser_ParsesV2ImplicitBodyWithMultipleStatements()
    {
        var diagnostics = new DiagnosticBag();
        var source = @"
§M[m001:Test]
§F[f001:Print:pub]
  §O[void]
  §E[cw]
  §C[Console.WriteLine]
    §A ""Line 1""
  §/C
  §C[Console.WriteLine]
    §A ""Line 2""
  §/C
§/F[f001]
§/M[m001]";
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);

        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Equal(2, module.Functions[0].Body.Count);
    }

    #endregion

    #region V2 Compact Types

    [Fact]
    public void V2AttributeHelper_ExpandsI32ToInt()
    {
        var result = V2AttributeHelper.ExpandType("i32");
        Assert.Equal("INT", result);
    }

    [Fact]
    public void V2AttributeHelper_ExpandsStrToString()
    {
        var result = V2AttributeHelper.ExpandType("str");
        Assert.Equal("STRING", result);
    }

    [Fact]
    public void V2AttributeHelper_ExpandsOptionType()
    {
        var result = V2AttributeHelper.ExpandType("?i32");
        Assert.Equal("OPTION[inner=INT]", result);
    }

    [Fact]
    public void V2AttributeHelper_ExpandsResultType()
    {
        var result = V2AttributeHelper.ExpandType("i32!str");
        Assert.Equal("RESULT[ok=INT][err=STRING]", result);
    }

    [Fact]
    public void V2AttributeHelper_ExpandsVoid()
    {
        var result = V2AttributeHelper.ExpandType("void");
        Assert.Equal("VOID", result);
    }

    [Fact]
    public void V2AttributeHelper_ExpandsBool()
    {
        var result = V2AttributeHelper.ExpandType("bool");
        Assert.Equal("BOOL", result);
    }

    #endregion

    #region V2 Bare Literals

    [Fact]
    public void Lexer_RecognizesBareInteger()
    {
        var tokens = Tokenize("42");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.IntLiteral, tokens[0].Kind);
        Assert.Equal(42, tokens[0].Value);
    }

    [Fact]
    public void Lexer_RecognizesBareNegativeInteger()
    {
        var tokens = Tokenize("-42");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.IntLiteral, tokens[0].Kind);
        Assert.Equal(-42, tokens[0].Value);
    }

    [Fact]
    public void Lexer_RecognizesBareFloat()
    {
        var tokens = Tokenize("3.14");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.FloatLiteral, tokens[0].Kind);
        Assert.Equal(3.14, tokens[0].Value);
    }

    [Fact]
    public void Lexer_RecognizesBareString()
    {
        var tokens = Tokenize("\"hello\"");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.StrLiteral, tokens[0].Kind);
        Assert.Equal("hello", tokens[0].Value);
    }

    [Fact]
    public void Lexer_RecognizesBareTrueLiteral()
    {
        var tokens = Tokenize("true");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.BoolLiteral, tokens[0].Kind);
        Assert.Equal(true, tokens[0].Value);
    }

    [Fact]
    public void Lexer_RecognizesBareFalseLiteral()
    {
        var tokens = Tokenize("false");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.BoolLiteral, tokens[0].Kind);
        Assert.Equal(false, tokens[0].Value);
    }

    #endregion

    #region V2 Effect Shortcodes

    [Fact]
    public void V2AttributeHelper_ExpandsConsoleWriteEffect()
    {
        var (category, value) = V2AttributeHelper.ExpandEffectCode("cw");
        Assert.Equal("io", category);
        Assert.Equal("console_write", value);
    }

    [Fact]
    public void V2AttributeHelper_ExpandsFileReadEffect()
    {
        var (category, value) = V2AttributeHelper.ExpandEffectCode("fr");
        Assert.Equal("io", category);
        Assert.Equal("file_read", value);
    }

    [Fact]
    public void V2AttributeHelper_ExpandsNetworkEffect()
    {
        var (category, value) = V2AttributeHelper.ExpandEffectCode("net");
        Assert.Equal("io", category);
        Assert.Equal("network", value);
    }

    [Fact]
    public void V2AttributeHelper_ExpandsRandomEffect()
    {
        var (category, value) = V2AttributeHelper.ExpandEffectCode("rand");
        Assert.Equal("nondeterminism", category);
        Assert.Equal("random", value);
    }

    #endregion

    #region v2 Lisp-Style Expressions

    [Fact]
    public void Lexer_RecognizesOpenParen()
    {
        var tokens = Tokenize("(");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.OpenParen, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesCloseParen()
    {
        var tokens = Tokenize(")");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.CloseParen, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesArrow()
    {
        var tokens = Tokenize("->");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Arrow, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesUnicodeArrow()
    {
        var tokens = Tokenize("→");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Arrow, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesPlusOperator()
    {
        var tokens = Tokenize("+");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Plus, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesMinusOperator()
    {
        var tokens = Tokenize("- ");  // space to avoid arrow or number

        Assert.Contains(tokens, t => t.Kind == TokenKind.Minus);
    }

    [Fact]
    public void Lexer_RecognizesStarOperator()
    {
        var tokens = Tokenize("*");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Star, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesDoubleStarOperator()
    {
        var tokens = Tokenize("**");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.StarStar, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesSlashOperator()
    {
        var tokens = Tokenize("/");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Slash, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesPercentOperator()
    {
        var tokens = Tokenize("%");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Percent, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesEqualEqualOperator()
    {
        var tokens = Tokenize("==");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.EqualEqual, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesBangEqualOperator()
    {
        var tokens = Tokenize("!=");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.BangEqual, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesLessOperator()
    {
        var tokens = Tokenize("<");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Less, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesLessEqualOperator()
    {
        var tokens = Tokenize("<=");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.LessEqual, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesGreaterOperator()
    {
        var tokens = Tokenize(">");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Greater, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesGreaterEqualOperator()
    {
        var tokens = Tokenize(">=");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.GreaterEqual, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesAmpAmpOperator()
    {
        var tokens = Tokenize("&&");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.AmpAmp, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesPipePipeOperator()
    {
        var tokens = Tokenize("||");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.PipePipe, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesLeftShiftOperator()
    {
        var tokens = Tokenize("<<");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.LessLess, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesRightShiftOperator()
    {
        var tokens = Tokenize(">>");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.GreaterGreater, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_TokenizesLispExpression()
    {
        var tokens = Tokenize("(== (% i 15) 0)");

        // ( == ( % i 15 ) 0 )
        Assert.Equal(TokenKind.OpenParen, tokens[0].Kind);
        Assert.Equal(TokenKind.EqualEqual, tokens[1].Kind);
        Assert.Equal(TokenKind.OpenParen, tokens[2].Kind);
        Assert.Equal(TokenKind.Percent, tokens[3].Kind);
        Assert.Equal(TokenKind.Identifier, tokens[4].Kind);
        Assert.Equal("i", tokens[4].Text);
        Assert.Equal(TokenKind.IntLiteral, tokens[5].Kind);
        Assert.Equal(TokenKind.CloseParen, tokens[6].Kind);
        Assert.Equal(TokenKind.IntLiteral, tokens[7].Kind);
        Assert.Equal(TokenKind.CloseParen, tokens[8].Kind);
    }

    [Fact]
    public void Parser_ParsesSimpleLispAddition()
    {
        var diagnostics = new DiagnosticBag();
        var source = @"
§M[m001:Test]
§F[f001:Add:pub]
  §O[i32]
  §BODY
    §RETURN (+ 1 2)
  §END_BODY
§/F[f001]
§/M[m001]";
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);

        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Single(module.Functions);
        var returnStmt = module.Functions[0].Body[0] as ReturnStatementNode;
        Assert.NotNull(returnStmt);
        Assert.IsType<BinaryOperationNode>(returnStmt!.Expression);
        var binOp = (BinaryOperationNode)returnStmt.Expression!;
        Assert.Equal(BinaryOperator.Add, binOp.Operator);
    }

    [Fact]
    public void Parser_ParsesNestedLispExpression()
    {
        var diagnostics = new DiagnosticBag();
        var source = @"
§M[m001:Test]
§F[f001:Calc:pub]
  §O[bool]
  §BODY
    §RETURN (== (% i 15) 0)
  §END_BODY
§/F[f001]
§/M[m001]";
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);

        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var returnStmt = module.Functions[0].Body[0] as ReturnStatementNode;
        Assert.NotNull(returnStmt);
        Assert.IsType<BinaryOperationNode>(returnStmt!.Expression);
        var binOp = (BinaryOperationNode)returnStmt.Expression!;
        Assert.Equal(BinaryOperator.Equal, binOp.Operator);
        // Left should be a modulo operation
        Assert.IsType<BinaryOperationNode>(binOp.Left);
        var modOp = (BinaryOperationNode)binOp.Left;
        Assert.Equal(BinaryOperator.Modulo, modOp.Operator);
    }

    [Fact]
    public void Parser_ParsesBareVariableInLispExpression()
    {
        var diagnostics = new DiagnosticBag();
        var source = @"
§M[m001:Test]
§F[f001:Test:pub]
  §I[i32:x]
  §O[i32]
  §BODY
    §RETURN (+ x 1)
  §END_BODY
§/F[f001]
§/M[m001]";
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);

        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var returnStmt = module.Functions[0].Body[0] as ReturnStatementNode;
        var binOp = (BinaryOperationNode)returnStmt!.Expression!;
        Assert.IsType<ReferenceNode>(binOp.Left);
        var refNode = (ReferenceNode)binOp.Left;
        Assert.Equal("x", refNode.Name);
    }

    [Fact]
    public void Parser_ParsesUnaryNot()
    {
        var diagnostics = new DiagnosticBag();
        var source = @"
§M[m001:Test]
§F[f001:Test:pub]
  §I[BOOL:flag]
  §O[BOOL]
  §BODY
    §RETURN (! flag)
  §END_BODY
§/F[f001]
§/M[m001]";
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);

        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var returnStmt = module.Functions[0].Body[0] as ReturnStatementNode;
        Assert.IsType<UnaryOperationNode>(returnStmt!.Expression);
        var unaryOp = (UnaryOperationNode)returnStmt.Expression!;
        Assert.Equal(UnaryOperator.Not, unaryOp.Operator);
    }

    #endregion

    #region v2 Print Alias

    [Fact]
    public void Lexer_RecognizesPrintAlias()
    {
        var tokens = Tokenize("§P");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Print, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesPrintFAlias()
    {
        var tokens = Tokenize("§Pf");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.PrintF, tokens[0].Kind);
    }

    [Fact]
    public void Parser_ParsesPrintStatement()
    {
        var diagnostics = new DiagnosticBag();
        var source = @"
§M[m001:Test]
§F[f001:Main:pub]
  §O[void]
  §BODY
    §P ""Hello World""
  §END_BODY
§/F[f001]
§/M[m001]";
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);

        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Single(module.Functions[0].Body);
        Assert.IsType<PrintStatementNode>(module.Functions[0].Body[0]);
        var printStmt = (PrintStatementNode)module.Functions[0].Body[0];
        Assert.True(printStmt.IsWriteLine);
    }

    [Fact]
    public void Parser_ParsesPrintWithLispExpression()
    {
        var diagnostics = new DiagnosticBag();
        var source = @"
§M[m001:Test]
§F[f001:Main:pub]
  §I[i32:x]
  §O[void]
  §BODY
    §P (+ x 1)
  §END_BODY
§/F[f001]
§/M[m001]";
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);

        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var printStmt = (PrintStatementNode)module.Functions[0].Body[0];
        Assert.IsType<BinaryOperationNode>(printStmt.Expression);
    }

    #endregion

    #region v2 Arrow Syntax

    [Fact]
    public void Parser_ParsesIfWithArrowSyntax()
    {
        var diagnostics = new DiagnosticBag();
        var source = @"
§M[m001:Test]
§F[f001:Main:pub]
  §I[BOOL:flag]
  §O[void]
  §BODY
    §IF[i1] flag → §P ""Yes""
    §/I[i1]
  §END_BODY
§/F[f001]
§/M[m001]";
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);

        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Single(module.Functions[0].Body);
        var ifStmt = module.Functions[0].Body[0] as IfStatementNode;
        Assert.NotNull(ifStmt);
        Assert.Single(ifStmt!.ThenBody);
        Assert.IsType<PrintStatementNode>(ifStmt.ThenBody[0]);
    }

    [Fact]
    public void Lexer_RecognizesElseIfShortcut()
    {
        var tokens = Tokenize("§EI");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.ElseIf, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesElseShortcut()
    {
        var tokens = Tokenize("§EL");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Else, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesWhileShortcut()
    {
        var tokens = Tokenize("§WH");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.While, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesEndWhileShortcut()
    {
        var tokens = Tokenize("§/WH");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.EndWhile, tokens[0].Kind);
    }

    #endregion

    #region v2 Implicit Closing

    [Fact]
    public void Parser_ParsesCallWithImplicitClosing()
    {
        var diagnostics = new DiagnosticBag();
        var source = @"
§M[m001:Test]
§F[f001:Main:pub]
  §O[void]
  §BODY
    §C[Console.WriteLine] ""Hello""
  §END_BODY
§/F[f001]
§/M[m001]";
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);

        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Single(module.Functions[0].Body);
        var callStmt = module.Functions[0].Body[0] as CallStatementNode;
        Assert.NotNull(callStmt);
        Assert.Single(callStmt!.Arguments);
    }

    [Fact]
    public void Parser_ParsesCallWithImplicitClosingAndLispExpression()
    {
        var diagnostics = new DiagnosticBag();
        var source = @"
§M[m001:Test]
§F[f001:Main:pub]
  §I[i32:x]
  §O[void]
  §BODY
    §C[Console.WriteLine] (+ x 1)
  §END_BODY
§/F[f001]
§/M[m001]";
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);

        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var callStmt = module.Functions[0].Body[0] as CallStatementNode;
        Assert.NotNull(callStmt);
        Assert.Single(callStmt!.Arguments);
        Assert.IsType<BinaryOperationNode>(callStmt.Arguments[0]);
    }

    #endregion

    #region v2 Backtick Identifiers

    [Fact]
    public void Lexer_RecognizesBacktickIdentifier()
    {
        var tokens = Tokenize("`true`");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Identifier, tokens[0].Kind);
        Assert.Equal("true", tokens[0].Text);
    }

    [Fact]
    public void Lexer_RecognizesBacktickIdentifierWithSpaces()
    {
        var tokens = Tokenize("`my var`");

        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Identifier, tokens[0].Kind);
        Assert.Equal("my var", tokens[0].Text);
    }

    #endregion
}
