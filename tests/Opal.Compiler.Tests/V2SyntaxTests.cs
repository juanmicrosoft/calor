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
}
