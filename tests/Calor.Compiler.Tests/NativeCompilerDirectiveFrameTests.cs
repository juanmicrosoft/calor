using System.Text;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class NativeCompilerDirectiveFrameTests
{
    [Theory]
    [InlineData("nullable-directive", "#nullable enable")]
    [InlineData("nullable-directive", "// \u03c0\U0001f600\r\n#nullable enable\r\n#nullable restore\r\n")]
    [InlineData("pragma", "#pragma warning disable CS0168\r\n#pragma warning restore CS0168")]
    [InlineData("pragma", "#pragma checksum \"file.cs\" \"{406EA660-64CF-4C82-B6F0-42D48172A799}\" \"0123456789abcdef\"")]
    [InlineData("warning-directive", "#warning retained message")]
    [InlineData("error-directive", "#error retained error")]
    [InlineData("line-directive", "#line 200 \"mapped.cs\"")]
    [InlineData("symbol-directive", "#define FEATURE\n#undef FEATURE")]
    public void DirectiveFrame_PreservesExactFeatureBytesAndFollowingItem(string feature, string payload)
    {
        var source = "module Directives\r\n" + Frame(feature, payload, "  ", "\r\n")
            + "  func Next () \u2192 int\r\n    return 7\r\n";
        var (module, diagnostics) = Parse(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var directive = Assert.IsType<CompilerDirectiveNode>(module.Items[0]);
        Assert.Equal(feature, directive.Feature);
        Assert.Equal(Encoding.UTF8.GetBytes(payload), Encoding.UTF8.GetBytes(directive.Code));
        Assert.Empty(module.InteropBlocks);
        Assert.Equal("Next", Assert.Single(module.Functions).Name);
        var token = Assert.Single(new Lexer(source, new DiagnosticBag()).TokenizeAll(),
            token => token.Kind == TokenKind.CompilerDirective);
        var frame = Assert.IsType<RawCSharpFrame>(token.Value);
        Assert.True(frame.IsCompilerDirective);
        Assert.Equal(payload, frame.Code);

        var emitted = new CalorEmitter().Emit(module);
        Assert.Contains("raw directive:" + feature, emitted);
        Assert.DoesNotContain("\u00a7CDIR", emitted);
        var (reparsed, reparsedDiagnostics) = Parse(emitted);
        Assert.False(reparsedDiagnostics.HasErrors, string.Join("\n", reparsedDiagnostics));
        var again = Assert.IsType<CompilerDirectiveNode>(reparsed.Items[0]);
        Assert.Equal(feature, again.Feature);
        Assert.Equal(payload, again.Code);
        Assert.Equal(payload, Assert.IsType<CompilerDirectiveNode>(
            Parse(RawCSharpFrame.NormalizeLineEndings(source)).Module.Items[0]).Code);
    }

    [Theory]
    [InlineData("nullable-directive", "")]
    [InlineData("nullable-directive", "// only a comment")]
    [InlineData("nullable-directive", "#nullable banana")]
    [InlineData("nullable-directive", "#nullable enable junk")]
    [InlineData("nullable-directive", "#nullable enable\nclass Hidden {}")]
    [InlineData("nullable-directive", "#nullable enable\nSystem.Console.WriteLine(1);")]
    [InlineData("nullable-directive", "#nullable enable\nusing System;")]
    [InlineData("nullable-directive", "#nullable enable\nextern alias Hidden;")]
    [InlineData("nullable-directive", "#nullable enable\n[assembly: System.CLSCompliant(true)]")]
    [InlineData("nullable-directive", "#nullable enable\n#if NEVER\nclass Hidden {}\n#endif")]
    [InlineData("nullable-directive", "#nullable enable\n#pragma warning disable CS0168")]
    [InlineData("nullable-directive", "#nullable enable\n/* unterminated")]
    [InlineData("nullable-directive", "#nullable enable\n;")]
    [InlineData("nullable-directive", "#nullable enable\n}")]
    [InlineData("nullable-directive", "#nullable enable\n#if false\narbitrary disabled text\n#endif")]
    [InlineData("nullable-directive", "#nullable enable\n#region Hidden\n#endregion")]
    [InlineData("pragma", "#pragma warning disable CS0168\nclass Hidden {}")]
    [InlineData("pragma", "#pragma warning disable 999999999999999999999999")]
    [InlineData("warning-directive", "#warning allowed\nclass Hidden {}")]
    [InlineData("error-directive", "#error allowed\nclass Hidden {}")]
    [InlineData("symbol-directive", "#define A\n#if A\n#undef A\n#endif")]
    [InlineData("line-directive", "#line 0")]
    public void DirectiveFrame_RejectsMalformedMismatchedAndExecutablePayloads(string feature, string payload)
    {
        var (_, diagnostics) = Parse("module Rejected\n" + Frame(feature, payload));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.InvalidRawFrame);
        Assert.True(Program.Compile("module Rejected\n" + Frame(feature, payload)).HasErrors);
    }

    [Theory]
    [InlineData("directive:")]
    [InlineData("directive:unknown")]
    [InlineData("directive:compiler-directive")]
    [InlineData("directive:nullable%ZZdirective")]
    [InlineData("directive:%FF")]
    [InlineData("directive:nullable-directive%3Aextra")]
    [InlineData("directive:%6Eullable-directive")]
    [InlineData("directive:nullable-directive:extra")]
    [InlineData("directive:nullable-directive%")]
    public void DirectiveFrame_RejectsUnknownOrNoncanonicalFeatureEncoding(string reason)
    {
        const string payload = "#nullable enable";
        var diagnostics = new DiagnosticBag();
        _ = new Lexer($"raw {reason} {Encoding.UTF8.GetByteCount(payload)}\n{payload}", diagnostics)
            .TokenizeAll();
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.InvalidRawFrame);
    }

    [Theory]
    [InlineData("  class Host\n", "    ")]
    [InlineData("  interface Host\n", "    ")]
    [InlineData("  func Host () \u2192 void\n", "    ")]
    [InlineData("  func Before () \u2192 int\n    return 1\n", "  ")]
    [InlineData("  using System\n", "  ")]
    public void SymbolDirective_RejectsNonCompilationUnitOrLatePlacement(string prefix, string indent)
    {
        var (_, diagnostics) = Parse("module Placement\n" + prefix
            + Frame("symbol-directive", "#define TOO_LATE", indent));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.InvalidRawFrame);
    }

    [Fact]
    public void DirectiveFrame_PreservesClassAndStatementOrderWithoutOpaqueEffects()
    {
        var source = "module Ordered\n  class Host\n"
            + "    func Before () \u2192 int\n      return 1\n"
            + Frame("pragma", "#pragma warning disable CS0168", "    ")
            + "    func After () \u2192 int\n"
            + Frame("nullable-directive", "#nullable enable", "      ")
            + "      return 2\n"
            + Frame("pragma", "#pragma warning restore CS0168", "    ");
        var (module, diagnostics) = Parse(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var type = Assert.Single(module.Classes);
        Assert.Collection(type.Items,
            item => Assert.IsType<MethodNode>(item),
            item => Assert.IsType<CompilerDirectiveNode>(item),
            item => Assert.IsType<MethodNode>(item),
            item => Assert.IsType<CompilerDirectiveNode>(item));
        Assert.IsType<CompilerDirectiveNode>(type.Methods[1].Body[0]);
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var generated = compiled.GeneratedCode;
        Assert.True(generated.IndexOf("Before(", StringComparison.Ordinal)
            < generated.IndexOf("#pragma warning disable", StringComparison.Ordinal));
        Assert.True(generated.IndexOf("#pragma warning disable", StringComparison.Ordinal)
            < generated.IndexOf("After(", StringComparison.Ordinal));
        Assert.True(generated.IndexOf("After(", StringComparison.Ordinal)
            < generated.IndexOf("#nullable enable", StringComparison.Ordinal));
        Assert.True(generated.IndexOf("#nullable enable", StringComparison.Ordinal)
            < generated.IndexOf("#pragma warning restore", StringComparison.Ordinal));
        var (reparsed, reparsedDiagnostics) = Parse(new CalorEmitter().Emit(module));
        Assert.False(reparsedDiagnostics.HasErrors, string.Join("\n", reparsedDiagnostics));
        Assert.IsType<CompilerDirectiveNode>(reparsed.Classes[0].Methods[1].Body[0]);
        Assert.True(GeneratedCSharpCompiler.Validate(generated).CompilationSuccess);
    }

    [Fact]
    public void DirectiveFrame_ControlsActualNullableCompilerState()
    {
        var source = "module NullableState\n"
            + Frame("nullable-directive", "#nullable enable")
            + "  func Probe (string:value) \u2192 string\n    return value\n";
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var enabled = CSharpCompilation.Create("NullableEnabled",
            [CSharpSyntaxTree.ParseText(compiled.GeneratedCode)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Disable));
        var method = enabled.SyntaxTrees.Single().GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Probe");
        var enabledSymbol = enabled.GetSemanticModel(method.SyntaxTree).GetDeclaredSymbol(method);
        Assert.NotNull(enabledSymbol);
        Assert.Equal(NullableAnnotation.NotAnnotated, enabledSymbol.ReturnNullableAnnotation);
        var disabled = enabled.RemoveAllSyntaxTrees().AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            compiled.GeneratedCode.Replace("#nullable enable", "#nullable disable", StringComparison.Ordinal)));
        var disabledMethod = disabled.SyntaxTrees.Single().GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Probe");
        var disabledSymbol = disabled.GetSemanticModel(disabledMethod.SyntaxTree).GetDeclaredSymbol(disabledMethod);
        Assert.NotNull(disabledSymbol);
        Assert.Equal(NullableAnnotation.None, disabledSymbol.ReturnNullableAnnotation);
        Assert.True(GeneratedCSharpCompiler.Validate(compiled.GeneratedCode).CompilationSuccess);
    }

    [Fact]
    public void DirectiveFrame_PreservesInterfaceScopeWithoutBecomingInterop()
    {
        var source = "module InterfaceDirectives\n  interface IHost\n"
            + Frame("nullable-directive", "#nullable enable", "    ");
        var (module, diagnostics) = Parse(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var type = Assert.Single(module.Interfaces);
        var directive = Assert.IsType<CompilerDirectiveNode>(Assert.Single(type.Items));
        Assert.Equal("#nullable enable", directive.Code);
        var (again, againDiagnostics) = Parse(new CalorEmitter().Emit(module));
        Assert.False(againDiagnostics.HasErrors, string.Join("\n", againDiagnostics));
        Assert.IsType<CompilerDirectiveNode>(Assert.Single(again.Interfaces[0].Items));
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Assert.True(GeneratedCSharpCompiler.Validate(compiled.GeneratedCode).CompilationSuccess);
    }

    [Fact]
    public void SymbolDirective_PreservesConditionalCompilationUnitPrefix()
    {
        var source = "module Symbols\n  pp FEATURE\n"
            + Frame("symbol-directive", "#define INNER", "    ")
            + "  func Probe () \u2192 int\n    return 1\n";
        var (module, diagnostics) = Parse(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var conditional = Assert.Single(module.TypePreprocessorBlocks);
        Assert.IsType<CompilerDirectiveNode>(Assert.Single(conditional.Items));
        var (again, againDiagnostics) = Parse(new CalorEmitter().Emit(module));
        Assert.False(againDiagnostics.HasErrors, string.Join("\n", againDiagnostics));
        Assert.IsType<CompilerDirectiveNode>(Assert.Single(again.TypePreprocessorBlocks[0].Items));
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        foreach (var symbols in new[] { Array.Empty<string>(), new[] { "FEATURE" } })
        {
            var validation = GeneratedCSharpCompiler.Validate(
                [new GeneratedCSharpSource(compiled.GeneratedCode, "symbols.g.cs")],
                new GeneratedCSharpCompilationContext { PreprocessorSymbols = symbols });
            Assert.True(validation.CompilationSuccess, string.Join("\n", validation.FormattedCompilationErrors));
        }
    }

    [Fact]
    public void DirectiveFrame_DoesNotChangeInteropCountsOrHideFollowingRawMember()
    {
        const string payload = "public int RawValue() => 3;";
        var source = "module Mixed\n" + Frame("nullable-directive", "#nullable enable")
            + $"  raw member {Encoding.UTF8.GetByteCount(payload)}\n{payload}\n";
        var (module, diagnostics) = Parse(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        Assert.IsType<CompilerDirectiveNode>(module.Items[0]);
        Assert.Equal(payload, Assert.Single(module.InteropBlocks).CSharpCode);
        Assert.Single(RawCSharpFrame.ReadTokens(source), token => token.Kind == TokenKind.CSharpInterop);
        Assert.Single(RawCSharpFrame.ReadTokens(source), token => token.Kind == TokenKind.CompilerDirective);
    }

    [Fact]
    public void Converter_PreservesOriginalNullableEnableWithoutWholeFileRescue()
    {
        const string csharp = "#nullable enable\npublic class DirectiveHost { public int Value() => 7; }";
        var conversion = new CSharpToCalorConverter().Convert(csharp, "DirectiveHost.cs");
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues.Select(issue => issue.Message)));
        Assert.DoesNotContain(conversion.Losses, loss => loss.Kind == ConversionLossKind.InteropPreserved);
        Assert.Contains("raw directive:nullable-directive 16", conversion.CalorSource);
        var (module, diagnostics) = Parse(conversion.CalorSource!);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var directive = Assert.IsType<CompilerDirectiveNode>(module.Items[0]);
        Assert.Equal("#nullable enable", directive.Code);
        Assert.Equal("nullable-directive", directive.Feature);
        Assert.Empty(module.InteropBlocks);
        var compiled = Program.Compile(conversion.CalorSource!);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Assert.True(GeneratedCSharpCompiler.Validate(compiled.GeneratedCode).CompilationSuccess);
    }

    [Fact]
    public void DirectiveEmitter_RejectsFabricatedExecutableOrMismatchedDirectiveAst()
    {
        Assert.Throws<InvalidOperationException>(() => new CompilerDirectiveNode(default,
            "#nullable enable\npublic class Hidden {}", "nullable-directive").Accept(new CalorEmitter()));
        Assert.Throws<InvalidOperationException>(() => new CompilerDirectiveNode(default,
            "#pragma warning disable CS0168", "nullable-directive").Accept(new CalorEmitter()));
    }

    [Fact]
    public void OrdinaryRawReasons_RemainOpaqueAndCannotBorrowDirectiveClassification()
    {
        foreach (var reason in new[] { "member", "nullable-directive", "directive", "Directive:nullable-directive:Other:~" })
        {
            var diagnostics = new DiagnosticBag();
            var tokens = new Lexer($"raw {reason} 16\n#nullable enable", diagnostics).TokenizeAll();
            if (reason.Contains(':'))
            {
                Assert.True(diagnostics.HasErrors);
                continue;
            }
            Assert.False(diagnostics.HasErrors);
            Assert.Equal(TokenKind.CSharpInterop, tokens[0].Kind);
            Assert.False(Assert.IsType<RawCSharpFrame>(tokens[0].Value).IsCompilerDirective);
        }
    }

    private static string Frame(string feature, string payload, string indent = "  ", string newline = "\n")
        => $"{indent}raw directive:{Uri.EscapeDataString(feature)} {Encoding.UTF8.GetByteCount(payload)}{newline}{payload}{newline}";

    private static (ModuleNode Module, DiagnosticBag Diagnostics) Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        return (module, diagnostics);
    }
}
