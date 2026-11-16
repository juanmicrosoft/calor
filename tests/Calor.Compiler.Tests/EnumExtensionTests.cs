using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class EnumExtensionTests
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
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    #region Lexer Tests

    [Fact]
    public void Lexer_RecognizesEnumKeyword()
    {
        var tokens = Tokenize("enum", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Enum, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void Lexer_RejectsLegacyEndEnumKeyword()
    {
        var tokens = Tokenize("§/ENUM", out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Error, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
        Assert.Contains("dedenting", Assert.Single(diagnostics.Errors).Message);
    }

    #endregion

    #region Parser Tests

    [Fact]
    public void Parser_ParsesBasicEnum()
    {
        var source = """
            module Test Test block
              enum Color
                Red
                Green
                Blue
            """;
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Single(module.Enums);
        var enumDef = module.Enums[0];
        Assert.Equal(enumDef.LocalId, enumDef.Id);
        Assert.Null(enumDef.ToolingId);
        Assert.Equal("Color", enumDef.Name);
        Assert.Equal(3, enumDef.Members.Count);
        Assert.Equal("Red", enumDef.Members[0].Name);
        Assert.Equal("Green", enumDef.Members[1].Name);
        Assert.Equal("Blue", enumDef.Members[2].Name);
    }

    [Fact]
    public void Parser_ParsesEnumWithValues()
    {
        var source = """
            module Test Test block
              enum StatusCode
                Ok = 200
                NotFound = 404
                Error = 500
            """;
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Single(module.Enums);
        var enumDef = module.Enums[0];
        Assert.Equal("StatusCode", enumDef.Name);
        Assert.Equal("200", enumDef.Members[0].Value);
        Assert.Equal("404", enumDef.Members[1].Value);
        Assert.Equal("500", enumDef.Members[2].Value);
    }

    [Fact]
    public void Parser_ParsesEnumWithUnderlyingType()
    {
        var source = """
            module Test Test block
              enum Flags byte
                None = 0
                Read = 1
                Write = 2
            """;
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Single(module.Enums);
        var enumDef = module.Enums[0];
        Assert.Equal("byte", enumDef.UnderlyingType);
    }

    #endregion

    #region Code Generation Tests

    [Fact]
    public void Emit_Enum_GeneratesEnumDeclaration()
    {
        var source = """
            module Test Test block
              enum Color
                Red
                Green
                Blue
            """;
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("public enum Color", code);
        Assert.Contains("Red,", code);
        Assert.Contains("Green,", code);
        Assert.Contains("Blue,", code);
    }

    [Fact]
    public void Emit_EnumWithValues_GeneratesEnumWithValues()
    {
        var source = """
            module Test Test block
              enum StatusCode
                Ok = 200
                NotFound = 404
            """;
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("Ok = 200,", code);
        Assert.Contains("NotFound = 404,", code);
    }

    [Fact]
    public void Emit_EnumExtension_GeneratesStaticExtensionClass()
    {
        var source = """
            module Test Test block
              module ColorExtensions
                func ToHex (this Color:self) → string
                  !
                  return "#FF0000"
            """;
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("public static class ColorExtensions", code);
        Assert.Contains("public static string ToHex(this Color self)", code);
        Assert.Contains("return \"#FF0000\";", code);
    }

    #endregion

    #region Calor Emitter Round-Trip Tests

    [Fact]
    public void CalorEmitter_Enum_EmitsNewSyntax()
    {
        var source = """
            module Test Test block
              enum Color
                Red
                Green
            """;
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var emitter = new CalorEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("enum Color", code);
        Assert.DoesNotContain("§/EN", code);
        // Should NOT contain old ENUM syntax
        Assert.DoesNotContain("§ENUM", code);
    }

    [Fact]
    public void CalorEmitter_EnumExtension_RoundTrips()
    {
        var source = """
            module Test Test block
              module ColorExtensions
                func ToHex (this Color:self) → string
                  !
                  return "hex"
            """;
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var emitter = new CalorEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("module ColorExtensions", code);
        Assert.DoesNotContain("§/EEXT", code);
    }
    #endregion
    #region Negative Tests - Error Cases

    [Fact]
    public void Parser_EnumExtension_EmptyLeadingName_ReportsError()
    {
        var source = """
            §M{Test}
              §EEXT{:Color}
                §F{ToHex:pub}
                    §I{Color:self}
                    §O{str}
                    §R "hex"
              §/EEXT
            """;
        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics.Errors, e => e.Code == DiagnosticCode.MissingRequiredAttribute);
    }

    [Fact]
    public void Parser_EnumExtension_MissingEnumName_ReportsError()
    {
        var source = """
            §M{Test}
              §EEXT{}
                §F{ToHex:pub}
                    §I{Color:self}
                    §O{str}
                    §R "hex"
              §/EEXT
            """;
        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics.Errors, e => e.Code == DiagnosticCode.MissingRequiredAttribute);
    }

    [Fact]
    public void Parser_Enum_SourceIdHeader_ReportsError()
    {
        var source = """
            §M{Test}
              §EN{e001:Color}
              Red
              Green
              §/EN
            """;
        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics.Errors, e => e.Code == DiagnosticCode.UnexpectedToken);
    }

    [Fact]
    public void Parser_EnumExtension_EmptyExtension_NoError()
    {
        // Empty extension is allowed (unusual but valid)
        var source = """
            module Test Test block
              module ColorExtensions
            """;
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Single(module.EnumExtensions);
        Assert.Empty(module.EnumExtensions[0].Methods);
    }

    #endregion
}
