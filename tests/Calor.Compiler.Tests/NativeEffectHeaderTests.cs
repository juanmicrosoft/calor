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
        // 0.30: Render reads Holder's static field Source (through ReadSource), which may run
        // Holder's static initialization (`new Derived()`), so it declares alloc as well.
        var callerRow = consoleWriteCaller ? "cw, alloc" : "alloc";
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
                  return Source.{{(dispatch == "base-qualified" ? "ReadBase" : "Read")}}()
            {{(chain ? $$"""
                func ReadAgain private static () → string
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

    [Theory]
    [InlineData("  func F () → void\n    return")]
    [InlineData("  class C\n    func F protected () → void\n      return")]
    [InlineData("  class C\n    func F protected internal () → void\n      return")]
    [InlineData("  class C\n    func F private protected () → void\n      return")]
    [InlineData("  interface I\n    func F () → void")]
    [InlineData("  delegate F () → void")]
    [InlineData("  class C abstract\n    func F internal abstract () → void")]
    public void BoundaryPolicy_MissingOwnRowIsAParseError(string declaration)
    {
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer("module Rows\n" + declaration, diagnostics)
            .TokenizeAllForParser(), diagnostics).Parse();
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.MissingBoundaryEffectRow);
    }

    [Theory]
    [InlineData("private", "")]
    [InlineData("private", "cw")]
    [InlineData("internal", "")]
    [InlineData("internal", "cw")]
    public void BoundaryPolicy_NonPublicOwnAnnotationsAreRejected(string visibility, string code)
    {
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer(
            $"module Rows\n  func F {visibility} () → void\n    ! {code}\n    return", diagnostics)
            .TokenizeAllForParser(), diagnostics).Parse();
        Assert.Contains(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.NonPublicEffectRow && diagnostic.Span.Line == 3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoundaryPolicy_CallbackContractsSurvivePrivateBodyInference(bool typing)
    {
        const string source = """
            module Rows
              func Apply private (Action:callback ! cw) → void
                callback()
              func Run (Action:callback ! cw) → void
                ! cw
                Apply(callback)
            """;
        var result = Program.Compile(source, null, new CompilationOptions
        {
            EnableTypeChecking = typing, StrictEffects = true, UnknownCallPolicy = UnknownCallPolicy.Strict
        });
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.Null(result.Ast!.Functions.Single(function => function.Name == "Apply").Effects);
    }

    [Fact]
    public void BoundaryPolicy_InferredOverrideBodyFitsItsDeclaredAbstractBoundary()
    {
        const string source = """
            module Rows
              class Base abstract
                func Write internal abstract () → void
                  ! cw
              class Derived
                extends Base
                func Write internal override () → void
                  System.Console.WriteLine(1)
            """;
        var result = Program.Compile(source);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
    }

    [Theory]
    [InlineData(false, "(Action:callback) → void\n    !\n    return")]
    [InlineData(true, "(Action:callback) → void\n    !\n    return")]
    [InlineData(false, "() → Func<int>\n    !\n    return () => 1")]
    [InlineData(true, "() → Func<int>\n    !\n    return () => 1")]
    public void BoundaryPolicy_CallbackPositionsRequireRowsInBothTypingModes(bool typing, string signature)
    {
        var result = Program.Compile("module Rows\n  func F " + signature, null, new CompilationOptions
        {
            EnableTypeChecking = typing
        });
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.MissingCallableEffectRow);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoundaryPolicy_ShadowedFuncAndActionAreNotCallbackTypes(bool typing)
    {
        const string source = """
            module Rows
              class Action
              class Func<T>
              func Use (Action:a, Func<int>:f) → void
                !
                return
              func Make () → Func<int>?
                !
                return null
            """;
        var result = Program.Compile(source, null, new CompilationOptions { EnableTypeChecking = typing });
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoundaryPolicy_ExplicitUnknownCanBeUnusedButNotInvoked(bool typing)
    {
        const string source = """
            module Rows
              func Take (Action:callback ! unknown) → void
                !
                return
            """;
        var options = new CompilationOptions { EnableTypeChecking = typing, StrictEffects = true };
        Assert.False(Program.Compile(source, null, options).HasErrors);
        var invoked = Program.Compile(source.Replace("    return", "    callback()"), null, options);
        Assert.Contains(invoked.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoundaryPolicy_UsingAliasesStillRequireActualCallbackRows(bool typing)
    {
        const string source = """
            module Rows
              using Handler = System.Action
              using Deferred = System.Func<int>
              func Take (Handler:callback) → void
                !
                return
              func Make () → Deferred
                !
                return () => 1
            """;
        var result = Program.Compile(source, null, new CompilationOptions { EnableTypeChecking = typing });
        Assert.Equal(2, result.Diagnostics.Errors.Count(diagnostic =>
            diagnostic.Code == DiagnosticCode.MissingCallableEffectRow));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoundaryPolicy_HigherOrderDelegatePositionsAlsoRequireRows(bool typing)
    {
        const string source = """
            module Rows
              delegate Higher (Action:callback) → Func<int>
                !
            """;
        var result = Program.Compile(source, null, new CompilationOptions { EnableTypeChecking = typing });
        Assert.Equal(2, result.Diagnostics.Errors.Count(diagnostic =>
            diagnostic.Code == DiagnosticCode.MissingCallableEffectRow));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoundaryPolicy_NestedDelegateReturningShadowedFuncIsNotHigherOrder(bool typing)
    {
        const string source = """
            module Rows
              class Owner
                class Func<T>
                delegate Higher () → Func<int>?
                  !
            """;
        var result = Program.Compile(source, null, new CompilationOptions { EnableTypeChecking = typing });
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
    }

    [Theory]
    [InlineData("+", "Box:value, int:other")]
    [InlineData("-", "Box:value")]
    [InlineData("implicit", "Box:value")]
    [InlineData("explicit", "Box:value")]
    public void BoundaryPolicy_PublicOperatorsRequireOwnRowsEvenWithoutOptionalChecks(
        string token, string parameters)
    {
        var source = $$"""
            module OperatorBoundary
              class Box
                op {{token}} ({{parameters}}) → int
                  return 1
            """;
        foreach (var checks in new[] { false, true })
        {
            var result = Program.Compile(source, null, new CompilationOptions
            {
                EnableTypeChecking = checks, EnforceEffects = checks,
                StrictEffects = checks, VerifyContracts = false
            });
            Assert.True(result.HasErrors);
            Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.MissingBoundaryEffectRow
                && diagnostic.Span.Line == 3);
            Assert.Empty(result.GeneratedCode);
        }
    }

    [Fact]
    public void BoundaryPolicy_PreprocessorOperatorsAlsoRequireOwnRows()
    {
        const string source = """
            module OperatorBoundary
              class Box
                pp OPERATOR
                  op implicit (Box:value) → int
                    return 1
            """;
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.Contains(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.MissingBoundaryEffectRow && diagnostic.Span.Line == 4);
    }

    [Theory]
    [InlineData("+", "Box:value, int:other", "", false)]
    [InlineData("implicit", "Box:value", "", false)]
    [InlineData("+", "Box:value, int:other", "", true)]
    [InlineData("implicit", "Box:value", "", true)]
    [InlineData("+", "Box:value, int:other", "cw", true)]
    [InlineData("implicit", "Box:value", "cw", true)]
    public void BoundaryPolicy_ExplicitOperatorRowsRoundTripAndChargeTheirBodies(
        string token, string parameters, string row, bool effectful)
    {
        var source = $$"""
            module OperatorBoundary
              class Box
                op {{token}} ({{parameters}}) → int
                  ! {{row}}
            {{(effectful ? "      System.Console.WriteLine(\"effect\")" : "")}}
                  return 1
            """;
        var module = Parse(source);
        var emitted = new CalorEmitter().Emit(module);
        foreach (var candidate in new[] { source, emitted })
        {
            Assert.Equal(module.Classes.Single().OperatorOverloads.Single().Effects!.Effects,
                Parse(candidate).Classes.Single().OperatorOverloads.Single().Effects!.Effects);
            foreach (var typing in new[] { false, true })
            {
                var result = Program.Compile(candidate, null, new CompilationOptions
                {
                    EnableTypeChecking = typing, StrictEffects = true,
                    UnknownCallPolicy = UnknownCallPolicy.Strict, VerifyContracts = false
                });
                Assert.Equal(effectful && row.Length == 0, result.HasErrors);
                if (result.HasErrors)
                {
                    Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                        diagnostic.Code == DiagnosticCode.ForbiddenEffect);
                    Assert.Empty(result.GeneratedCode);
                }
            }
        }
    }

    [Fact]
    public void BoundaryPolicy_OperatorAstDoesNotBorrowImplicitAccessorAuthority()
    {
        const string source = """
            module OperatorBoundary
              class Box
                op implicit (Box:value) → int
                  ! cw
                  System.Console.WriteLine("effect")
                  return 1
            """;
        var module = Parse(source);
        module.Classes.Single().OperatorOverloads.Single().Effects = null;
        var diagnostics = new DiagnosticBag();
        new EffectEnforcementPass(diagnostics, UnknownCallPolicy.Strict).Enforce(module);
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return module;
    }
}
