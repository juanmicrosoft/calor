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

    [Theory]
    [InlineData("virtual", false, false)]
    [InlineData("virtual", true, false)]
    [InlineData("virtual", false, true)]
    [InlineData("virtual", true, true)]
    [InlineData("virtual-pure-override", false, false)]
    [InlineData("virtual-pure-override", true, false)]
    [InlineData("virtual-pure-override", false, true)]
    [InlineData("virtual-pure-override", true, true)]
    [InlineData("nonvirtual", false, false)]
    [InlineData("nonvirtual", true, false)]
    [InlineData("nonvirtual", false, true)]
    [InlineData("nonvirtual", true, true)]
    [InlineData("sealed-receiver", false, false)]
    [InlineData("sealed-receiver", true, false)]
    [InlineData("sealed-receiver", false, true)]
    [InlineData("sealed-receiver", true, true)]
    [InlineData("sealed-override", false, false)]
    [InlineData("sealed-override", true, false)]
    [InlineData("sealed-override", false, true)]
    [InlineData("sealed-override", true, true)]
    [InlineData("base-qualified", false, false)]
    [InlineData("base-qualified", true, false)]
    [InlineData("base-qualified", false, true)]
    [InlineData("base-qualified", true, true)]
    public void OrdinarySourceDispatchPreservesSlotAndImplementationRows(
        string dispatch, bool chain, bool consoleWriteCaller)
    {
        var callerRow = consoleWriteCaller ? "cw" : "";
        var derivedRow = dispatch is "virtual" or "base-qualified" ? "cw" : "";
        var source = $$"""
            module OrdinaryDispatch
              class Base
                func Read {{(dispatch == "nonvirtual" ? "" : "virtual")}} () → string
                  ! cw
                  return "safe"
              class Derived {{(dispatch == "sealed-receiver" ? "sealed" : "")}}
                extends Base
            {{(dispatch == "nonvirtual" ? "" : $$"""
                func Read {{(dispatch == "sealed-override" ? "sealed " : "")}}override () → string
                  ! {{derivedRow}}
            {{(derivedRow == "" ? "" : "      System.Console.WriteLine(\"effect\")")}}
                  return "effect"
            """)}}
            {{(dispatch != "base-qualified" ? "" : """
                func ReadBase () → string
                  !
                  return base.Read()
            """)}}
              class Holder
                field {{(dispatch is "sealed-receiver" or "sealed-override" or "base-qualified" ? "Derived" : "Base")}}:Source private static = new Derived()
                func ReadSource private static () → string
                  ! {{callerRow}}
                  return Source.{{(dispatch == "base-qualified" ? "ReadBase" : "Read")}}()
            {{(chain ? $$"""
                func ReadAgain private static () → string
                  ! {{callerRow}}
                  return ReadSource()
            """ : "")}}
                func Render static () → string
                  ! {{callerRow}}
                  return {{(chain ? "ReadAgain()" : "ReadSource()")}}
            """;
        foreach (var typing in new[] { false, true })
        {
            var result = Program.Compile(source, "ordinary-dispatch.calr", new CompilationOptions
            {
                EnableTypeChecking = typing, EnforceEffects = true, StrictEffects = true,
                UnknownCallPolicy = UnknownCallPolicy.Strict, UnsafeTranspileOnly = false,
                VerifyContracts = false, StatusWriter = TextWriter.Null
            });
            Assert.Equal(dispatch is "virtual" or "virtual-pure-override" && !consoleWriteCaller, result.HasErrors);
            if (result.HasErrors)
            {
                Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                    diagnostic.Code == DiagnosticCode.ForbiddenEffect
                    && diagnostic.Message.Contains("'cw'", StringComparison.Ordinal));
                Assert.Empty(result.GeneratedCode);
            }
            else
                Assert.Empty(result.Diagnostics.Errors);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinarySourceDispatchPreservesModuleCallers(bool interfaceReceiver)
    {
        var source = interfaceReceiver ? """
            module InterfaceCaller Test
              interface IShape
                func Describe () → void
                  ! cw
              func Draw (IShape:shape) → void
                ! cw
                shape.Describe()
            """ : """
            module ModuleCaller Test
              func Helper private (int:x) → int
                !
                return x + 1
              func Main () → int
                !
                return Helper(1)
            """;
        foreach (var typing in new[] { false, true })
        {
            var result = Program.Compile(source, "ordinary-module-caller.calr", new CompilationOptions
            {
                EnableTypeChecking = typing, EnforceEffects = true, StrictEffects = true,
                UnknownCallPolicy = UnknownCallPolicy.Strict, UnsafeTranspileOnly = false,
                VerifyContracts = false, StatusWriter = TextWriter.Null
            });
            Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Errors));
        }
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return module;
    }
}
