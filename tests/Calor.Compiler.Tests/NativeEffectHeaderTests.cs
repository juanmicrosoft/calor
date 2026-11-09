using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class NativeEffectHeaderTests
{
    public static IEnumerable<object[]> Codes => EffectCodes.KnownCompactCodes.Select(code => new object[] { code });

    [Theory]
    [MemberData(nameof(Codes))]
    public void AuthoritativeCodesRemainHeaderDataEvenWhenTheyAreKeywords(string code)
    {
        var module = Parse($"module Effects\n  func F () → void\n    ! {code}\n    return");
        var function = module.Functions.Single();
        Assert.Single(function.Effects!.Effects);
        Assert.IsType<ReturnStatementNode>(Assert.Single(function.Body));
        var reparse = Parse(new CalorEmitter().Emit(module)).Functions.Single();
        Assert.Equal(function.Effects.Effects, reparse.Effects!.Effects);
        Assert.IsType<ReturnStatementNode>(Assert.Single(reparse.Body));
    }

    [Fact]
    public void MutationAndThrowAreNotExecutableHeaderStatements()
    {
        var source = "module Effects\n  func F () → void\n    ! mut,throw\n    return";
        var result = Program.Compile(source);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        var function = result.Ast!.Functions.Single();
        Assert.Equal(2, function.Effects!.Effects.Count);
        Assert.IsType<ReturnStatementNode>(Assert.Single(function.Body));
    }

    [Fact]
    public void UnknownKeywordCodeIsDiagnosedRatherThanExecuted()
    {
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer("module Bad\n  func F () → void\n    ! return", diagnostics)
            .TokenizeAllForParser(), diagnostics).Parse();
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownEffectCode);
    }

    [Theory]
    [InlineData("mut")]
    [InlineData("net")]
    [InlineData("db")]
    [InlineData("env")]
    public void CommasDoNotBecomeColonsAfterTaxonomyPrefixes(string prefix)
    {
        var source = $"module Effects\n  func F () → void\n    ! {prefix},throw,fs:w\n    return";
        var function = Parse(source).Functions.Single();
        Assert.Equal("intentional", function.Effects!.Effects["exception"]);
        Assert.Contains("filesystem_write", function.Effects.Effects["io"]);
        Assert.IsType<ReturnStatementNode>(Assert.Single(function.Body));
        var emitted = new CalorEmitter().Emit(Parse(source));
        Assert.Equal(function.Effects.Effects, Parse(emitted).Functions.Single().Effects!.Effects);
    }

    [Fact]
    public void UnknownKeywordSuffixRetainsItsTaxonomyDiagnostic()
    {
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer("module Bad\n  func F () → void\n    ! fs:throw", diagnostics)
            .TokenizeAllForParser(), diagnostics).Parse();
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownEffectCode
            && diagnostic.Message.Contains("fs:throw"));
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return module;
    }
}
