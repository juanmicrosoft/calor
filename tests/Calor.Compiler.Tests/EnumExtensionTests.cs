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
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    #region Lexer Tests

    [Fact]
    public void Lexer_RecognizesEnumKeyword()
    {
        var tokens = Tokenize("§EN", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Enum, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void Lexer_RecognizesLegacyEnumKeyword()
    {
        var tokens = Tokenize("§ENUM", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.Enum, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void Lexer_RecognizesEndEnumKeyword()
    {
        var tokens = Tokenize("§/EN", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.EndEnum, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void Lexer_RecognizesLegacyEndEnumKeyword()
    {
        var tokens = Tokenize("§/ENUM", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.EndEnum, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void Lexer_RecognizesEnumExtensionKeyword()
    {
        var tokens = Tokenize("§EXT", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.EnumExtension, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void Lexer_RecognizesEndEnumExtensionKeyword()
    {
        var tokens = Tokenize("§/EXT", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.EndEnumExtension, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    #endregion

    #region Parser Tests

    [Fact]
    public void Parser_ParsesBasicEnum()
    {
        var source = """
            §M{m001:Test}
            §EN{e001:Color}
            Red
            Green
            Blue
            §/EN{e001}
            §/M{m001}
            """;
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Single(module.Enums);
        var enumDef = module.Enums[0];
        Assert.Equal("e001", enumDef.Id);
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
            §M{m001:Test}
            §EN{e001:StatusCode}
            Ok = 200
            NotFound = 404
            Error = 500
            §/EN{e001}
            §/M{m001}
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
            §M{m001:Test}
            §EN{e001:Flags:u8}
            None = 0
            Read = 1
            Write = 2
            §/EN{e001}
            §/M{m001}
            """;
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Single(module.Enums);
        var enumDef = module.Enums[0];
        Assert.Equal("u8", enumDef.UnderlyingType);
    }

    [Fact]
    public void Parser_ParsesEnumExtension_SingleMethod()
    {
        var source = """
            §M{m001:Test}
            §EXT{ext001:Color}
              §F{f001:ToHex:pub}
                §I{Color:self}
                §O{str}
                §R "hex"
              §/F{f001}
            §/EXT{ext001}
            §/M{m001}
            """;
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Single(module.EnumExtensions);
        var ext = module.EnumExtensions[0];
        Assert.Equal("ext001", ext.Id);
        Assert.Equal("Color", ext.EnumName);
        Assert.Single(ext.Methods);
        Assert.Equal("ToHex", ext.Methods[0].Name);
    }

    [Fact]
    public void Parser_ParsesEnumExtension_MultipleMethods()
    {
        var source = """
            §M{m001:Test}
            §EXT{ext001:Color}
              §F{f001:ToHex:pub}
                §I{Color:self}
                §O{str}
                §R "#000000"
              §/F{f001}
              §F{f002:IsPrimary:pub}
                §I{Color:self}
                §O{bool}
                §R true
              §/F{f002}
            §/EXT{ext001}
            §/M{m001}
            """;
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var ext = module.EnumExtensions[0];
        Assert.Equal(2, ext.Methods.Count);
        Assert.Equal("ToHex", ext.Methods[0].Name);
        Assert.Equal("IsPrimary", ext.Methods[1].Name);
    }

    #endregion

    #region Code Generation Tests

    [Fact]
    public void Emit_Enum_GeneratesEnumDeclaration()
    {
        var source = """
            §M{m001:Test}
            §EN{e001:Color}
            Red
            Green
            Blue
            §/EN{e001}
            §/M{m001}
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
            §M{m001:Test}
            §EN{e001:StatusCode}
            Ok = 200
            NotFound = 404
            §/EN{e001}
            §/M{m001}
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
            §M{m001:Test}
            §EXT{ext001:Color}
              §F{f001:ToHex:pub}
                §I{Color:self}
                §O{str}
                §R "#FF0000"
              §/F{f001}
            §/EXT{ext001}
            §/M{m001}
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
            §M{m001:Test}
            §EN{e001:Color}
            Red
            Green
            §/EN{e001}
            §/M{m001}
            """;
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var emitter = new CalorEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("§EN{e001:Color}", code);
        Assert.Contains("§/EN{e001}", code);
        // Should NOT contain old ENUM syntax
        Assert.DoesNotContain("§ENUM", code);
    }

    [Fact]
    public void CalorEmitter_EnumExtension_RoundTrips()
    {
        var source = """
            §M{m001:Test}
            §EXT{ext001:Color}
              §F{f001:ToHex:pub}
                §I{Color:self}
                §O{str}
                §R "hex"
              §/F{f001}
            §/EXT{ext001}
            §/M{m001}
            """;
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var emitter = new CalorEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("§EXT{ext001:Color}", code);
        Assert.Contains("§/EXT{ext001}", code);
    }

    #endregion

    #region Negative Tests - Error Cases

    [Fact]
    public void Parser_EnumExtension_MissingEnumParameter_ReportsError()
    {
        var source = """
            §M{m001:Test}
            §EXT{ext001:Color}
              §F{f001:BadMethod:pub}
                §I{i32:x}
                §O{str}
                §R "bad"
              §/F{f001}
            §/EXT{ext001}
            §/M{m001}
            """;
        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Single(diagnostics.Errors);
        var error = diagnostics.Errors[0];
        Assert.Equal(DiagnosticCode.MissingExtensionSelf, error.Code);
        Assert.Contains("BadMethod", error.Message);
        Assert.Contains("Color", error.Message);
    }

    [Fact]
    public void Parser_EnumExtension_MismatchedCloseId_ReportsError()
    {
        var source = """
            §M{m001:Test}
            §EXT{ext001:Color}
              §F{f001:ToHex:pub}
                §I{Color:self}
                §O{str}
                §R "hex"
              §/F{f001}
            §/EXT{ext002}
            §/M{m001}
            """;
        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics.Errors, e => e.Code == DiagnosticCode.MismatchedId);
    }

    [Fact]
    public void Parser_EnumExtension_MissingId_ReportsError()
    {
        var source = """
            §M{m001:Test}
            §EXT{:Color}
              §F{f001:ToHex:pub}
                §I{Color:self}
                §O{str}
                §R "hex"
              §/F{f001}
            §/EXT{}
            §/M{m001}
            """;
        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics.Errors, e => e.Code == DiagnosticCode.MissingRequiredAttribute);
    }

    [Fact]
    public void Parser_EnumExtension_MissingEnumName_ReportsError()
    {
        var source = """
            §M{m001:Test}
            §EXT{ext001}
              §F{f001:ToHex:pub}
                §I{Color:self}
                §O{str}
                §R "hex"
              §/F{f001}
            §/EXT{ext001}
            §/M{m001}
            """;
        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics.Errors, e => e.Code == DiagnosticCode.MissingRequiredAttribute);
    }

    [Fact]
    public void Parser_EnumExtension_MultipleMethods_OneMissingEnumParam_ReportsOneError()
    {
        var source = """
            §M{m001:Test}
            §EXT{ext001:Color}
              §F{f001:ToHex:pub}
                §I{Color:self}
                §O{str}
                §R "hex"
              §/F{f001}
              §F{f002:BadMethod:pub}
                §I{i32:value}
                §O{bool}
                §R true
              §/F{f002}
            §/EXT{ext001}
            §/M{m001}
            """;
        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        var extensionErrors = diagnostics.Errors.Where(e => e.Code == DiagnosticCode.MissingExtensionSelf).ToList();
        Assert.Single(extensionErrors);
        Assert.Contains("BadMethod", extensionErrors[0].Message);
    }

    [Fact]
    public void Parser_Enum_MismatchedCloseId_ReportsError()
    {
        var source = """
            §M{m001:Test}
            §EN{e001:Color}
            Red
            Green
            §/EN{e002}
            §/M{m001}
            """;
        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics.Errors, e => e.Code == DiagnosticCode.MismatchedId);
    }

    [Fact]
    public void Parser_EnumExtension_EmptyExtension_NoError()
    {
        // Empty extension is allowed (unusual but valid)
        var source = """
            §M{m001:Test}
            §EXT{ext001:Color}
            §/EXT{ext001}
            §/M{m001}
            """;
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Single(module.EnumExtensions);
        Assert.Empty(module.EnumExtensions[0].Methods);
    }

    [Fact]
    public void Parser_EnumExtension_CaseInsensitiveEnumTypeMatch()
    {
        // Type matching should be case-insensitive
        var source = """
            §M{m001:Test}
            §EXT{ext001:Color}
              §F{f001:ToHex:pub}
                §I{color:self}
                §O{str}
                §R "hex"
              §/F{f001}
            §/EXT{ext001}
            §/M{m001}
            """;
        var module = Parse(source, out var diagnostics);

        // Should not have missing extension self error due to case-insensitive match
        Assert.DoesNotContain(diagnostics.Errors, e => e.Code == DiagnosticCode.MissingExtensionSelf);
    }

    #endregion
}
