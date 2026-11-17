using Calor.Compiler;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Enforcement.Tests;

public sealed class UnknownCallbackContractTests
{
    [Theory]
    [InlineData("", true, false)]
    [InlineData(" ! unknown", true, false)]
    [InlineData(" !", false, true)]
    [InlineData(" ! cw", false, false)]
    public void CallbackContractsBindToTheirActualLatticeElement(string suffix, bool unknown, bool pure)
    {
        var ast = Parse($$"""
            module Contracts
              func Keep (Func<int>:callback{{suffix}}) → Func<int>{{suffix}}
                !
                return callback
            """);
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics, "contracts.calr").Bind(ast);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var function = Assert.Single(bound.Functions).Symbol;
        var parameter = Assert.Single(function.Parameters).FunctionType!;
        Assert.Equal(unknown, parameter.Row.IsUnknown);
        Assert.Equal(pure, parameter.Row.Equals(EffectRow.Pure));
        Assert.Equal(parameter.Row, function.ReturnFunctionType!.Row);
        if (!unknown && !pure)
            Assert.Equal(EffectRow.Concrete(["io:console_write"]), parameter.Row);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ! unknown")]
    [InlineData(" !")]
    [InlineData(" ! cw")]
    public void NestedCallbackParameterReturnAndDelegateShapesRoundTrip(string suffix)
    {
        var ast = Parse($$"""
            module Contracts
              delegate Factory (Action:callback{{suffix}}) → Func<Func<int>>{{suffix}}
                {{ExplicitContract(suffix).TrimStart()}}
              class Holder
                func Keep (Func<Action,Func<int>>:callback{{suffix}}) → Func<Action,Func<int>>{{suffix}}
                  !
                  return callback
            """);
        var emitted = new CalorEmitter().Emit(ast);
        var reparsed = Parse(emitted);
        Assert.Equal(emitted, new CalorEmitter().Emit(reparsed));
        var definition = Assert.Single(reparsed.Delegates);
        var method = Assert.Single(Assert.Single(reparsed.Classes).Methods);
        Assert.Equal(ast.Delegates.Single().Parameters.Single().TypeName,
            definition.Parameters.Single().TypeName);
        Assert.Equal(ast.Delegates.Single().Output!.TypeName, definition.Output!.TypeName);
        Assert.NotNull(definition.Effects);
        Assert.Equal(suffix.Length == 0 || suffix.Contains("unknown"), definition.Effects.IsUnknown);
        Assert.Equal(suffix.Length == 0, method.Parameters.Single().Row == null);
        Assert.Equal(suffix.Contains("unknown"), method.Output!.Row?.IsUnknown == true);
        Assert.Empty(method.Effects!.Effects);
    }

    [Fact]
    public void KnownOuterRowDoesNotInventNestedCallbackContracts()
    {
        var ast = Parse("""
            module Contracts
              func Keep (Func<Action,int>:callback ! cw) → void
                !
                return
            """);
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics, "contracts.calr").Bind(ast);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var callback = Assert.Single(Assert.Single(bound.Functions).Symbol.Parameters).FunctionType!;
        Assert.False(callback.Row.IsUnknown);
        Assert.True(Assert.Single(callback.ParameterRows).IsUnknown);
    }

    [Theory]
    [InlineData("Func<int>", "")]
    [InlineData("Factory", "")]
    [InlineData("Factory", " ! unknown")]
    [InlineData("Factory", " !")]
    [InlineData("Factory", " ! cw")]
    [InlineData("Callback", "")]
    [InlineData("Callback", " ! unknown")]
    [InlineData("Callback", " !")]
    [InlineData("Callback", " ! cw")]
    public void NamedDelegatesAndClosedGenericAliasesKeepCallbackContracts(string type, string suffix)
    {
        var source = $$"""
            module Contracts
              using Callback = System.Func<int>
              delegate Factory () → int
                ! unknown
              func Run ({{type}}:callback{{ExplicitContract(suffix)}}) → int
                !
                let {{type}}:alias = callback
                return alias.Invoke()
            """;
        var result = Compile(source);
        Assert.DoesNotContain(result.Diagnostics.Errors, d =>
            string.Compare(d.Code, "Calor0300", StringComparison.Ordinal) < 0);
        if (suffix == " !")
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        else
            Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.ForbiddenEffect);
        Assert.Equal(suffix is "" or " ! unknown",
            result.Diagnostics.Any(d => d.Code == DiagnosticCode.EffectRowUnknown));
    }

    [Theory]
    [InlineData("")]
    [InlineData("! unknown")]
    [InlineData("!")]
    [InlineData("! cw")]
    public void DelegateContractSpellingDoesNotChangeCallbackOmission(string contract)
    {
        var result = Compile($$"""
            module Contracts
              delegate Factory () → int
                {{(contract.Length == 0 ? "! unknown" : contract)}}
              func Run (Factory:callback ! unknown) → int
                !
                return callback()
            """);
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.ForbiddenEffect);
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Fact]
    public void KnownLambdaAndMethodRowsSurviveOmittedBindingRows()
    {
        var source = """
            module Contracts
              func Loud private () → int
                Console.WriteLine("loud")
                return 1
              func Run () → int
                !
                let Func<int>:callback = Loud
                return callback()
            """;
        var result = Compile(source);
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.ForbiddenEffect
            && d.Message.Contains("'cw'"));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
        var lambda = source.Replace("= Loud", "= () =>\n      Console.WriteLine(\"loud\")\n      return 1",
            StringComparison.Ordinal);
        var lambdaResult = Compile(lambda);
        Assert.Contains(lambdaResult.Diagnostics.Errors, d => d.Code == DiagnosticCode.ForbiddenEffect
            && d.Message.Contains("'cw'"));
        Assert.DoesNotContain(lambdaResult.Diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Fact]
    public void CallbackOmissionDoesNotGrantUnknownToTheCallablesOwnRow()
    {
        var result = Compile("""
            module Contracts
              func Run (Func<int>:callback ! unknown) → int
                !
                return callback()
            """);
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.ForbiddenEffect);
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.EffectRowUnknown);
        var known = Compile("""
            module Contracts
              func Run () → void
                !
                Console.WriteLine("still undeclared")
            """, UnknownCallPolicy.Permissive);
        Assert.Contains(known.Diagnostics.Errors, d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void ExistingUnknownEffectVariableKeepsItsBindingIdentity()
    {
        var ast = Parse("""
            module Contracts
              func Run <! unknown> (Func<int>:callback ! unknown) → int
                ! unknown
                return callback()
            """);
        var reparsed = Parse(new CalorEmitter().Emit(ast));
        var function = Assert.Single(reparsed.Functions);
        var row = Assert.Single(function.Parameters).Row!;
        Assert.False(row.IsUnknown);
        Assert.Equal("unknown", Assert.Single(row.EffectVariables));
        Assert.Equal(0, Assert.Single(row.EffectVariableOrdinals));
        Assert.False(function.Effects!.IsUnknown);
        Assert.Equal(0, Assert.Single(function.Effects.EffectVariableOrdinals));
        Assert.False(Compile(new CalorEmitter().Emit(reparsed)).HasErrors);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ! unknown")]
    public void UnusedUnknownDoesNotExecuteTheCallback(string suffix)
    {
        var result = Compile($$"""
            module Contracts
              func Ignore (Func<int>:callback{{ExplicitContract(suffix)}}) → int
                !
                return 1
            """);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ! unknown")]
    [InlineData(" !")]
    [InlineData(" ! cw")]
    public void InvocationDistinguishesUnknownPureAndKnown(string suffix)
    {
        var result = Compile($$"""
            module Contracts
              func Run (Func<int>:callback{{ExplicitContract(suffix)}}) → int
                !
                return callback()
            """);
        if (suffix == " !")
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        else
            Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.ForbiddenEffect);
        Assert.Equal(suffix is "" or " ! unknown",
            result.Diagnostics.Any(d => d.Code == DiagnosticCode.EffectRowUnknown));
        if (suffix == " ! unknown")
            Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown
                && d.Message.Contains("explicitly declares Unknown effects"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ! unknown")]
    public void UnknownInvocationCannotBeCoveredByAllKnownEffects(string suffix)
    {
        var result = Compile($$"""
            module Contracts
              func Run (Func<int>:callback{{ExplicitContract(suffix)}}) → int
                ! cw,cr,fs:rw,net:rw,db:rw,env:rw,mut,alloc,throw
                return callback()
            """);
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.ForbiddenEffect
            && d.Message.Contains("'unknown'"));
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ! unknown")]
    public void PermissiveWaivesUnknownButNeverKnownUndeclaredEffects(string suffix)
    {
        var source = $$"""
            module Contracts
              func Run (Func<int>:callback{{ExplicitContract(suffix)}}) → int
                !
                return callback()
            """;
        Assert.False(Compile(source, UnknownCallPolicy.Permissive).HasErrors);
        var known = source.Replace(ExplicitContract(suffix) + ") →", " ! cw) →", StringComparison.Ordinal);
        Assert.Contains(Compile(known, UnknownCallPolicy.Permissive).Diagnostics.Errors,
            d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ! unknown")]
    public void TransportedUnknownStillRequiresAProvableFit(string suffix)
    {
        var source = $$"""
            module Contracts
              func Keep (Func<int>:callback{{ExplicitContract(suffix)}}) → Func<int>{{ExplicitContract(suffix)}}
                !
                return callback
            """;
        var strict = Compile(source);
        Assert.Contains(strict.Diagnostics.Errors, d => d.Code == DiagnosticCode.EffectRowUnknown);
        Assert.DoesNotContain(strict.Diagnostics, d => d.Code == DiagnosticCode.ForbiddenEffect);
        Assert.False(Compile(source, UnknownCallPolicy.Permissive).HasErrors);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ! unknown")]
    [InlineData(" !")]
    [InlineData(" ! cw")]
    public void ClosedGenericAliasReturnContractsCannotEraseUnknown(string suffix)
    {
        var source = $$"""
            module Contracts
              using Callback = System.Func<int>
              func Keep (Callback:callback{{ExplicitContract(suffix)}}) → Callback{{ExplicitContract(suffix)}}
                !
                return callback
            """;
        var strict = Compile(source);
        Assert.Equal(suffix is "" or " ! unknown",
            strict.Diagnostics.Errors.Any(d => d.Code == DiagnosticCode.EffectRowUnknown));
        Assert.DoesNotContain(strict.Diagnostics, d => d.Code == DiagnosticCode.ForbiddenEffect);
        if (suffix is " !" or " ! cw")
            Assert.False(strict.HasErrors, string.Join("\n", strict.Diagnostics));
        Assert.False(Compile(source, UnknownCallPolicy.Permissive).HasErrors);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ! unknown")]
    public void UnknownSurvivesBindingAliasesAndReturnedCallbacks(string suffix)
    {
        var result = Compile($$"""
            module Contracts
              func Keep (Func<int>:callback{{ExplicitContract(suffix)}}) → Func<int>{{ExplicitContract(suffix)}}
                !
                return callback
              func Run (Func<int>:callback{{ExplicitContract(suffix)}}) → int
                !
                let Func<int>:alias = Keep(callback)
                let Func<int>:second = alias
                let Func<int>:third = second
                return third()
            """);
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.ForbiddenEffect);
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Theory]
    [InlineData("Func<int>", "")]
    [InlineData("Func<int>", " ! unknown")]
    [InlineData("Factory", "")]
    [InlineData("Factory", " ! unknown")]
    [InlineData("Callback", "")]
    [InlineData("Callback", " ! unknown")]
    public void UnknownParameterAliasChainsNeverAcquireKnownOrPurePermissions(string type, string suffix)
    {
        var source = $$"""
            module Contracts
              using Callback = System.Func<int>
              delegate Factory () → int
                ! unknown
              func Run ({{type}}:callback{{ExplicitContract(suffix)}}) → int
                ! cw
                let {{type}}:first = callback
                let {{type}}:second = first
                let {{type}}:third = second
                return third()
            """;
        var strict = Compile(source);
        Assert.Contains(strict.Diagnostics.Errors, d => d.Code == DiagnosticCode.ForbiddenEffect
            && d.Message.Contains("'unknown'"));
        Assert.Contains(strict.Diagnostics.Errors, d => d.Code == DiagnosticCode.EffectRowUnknown);
        var pureDestination = Compile(source.Replace($"{type}:third =", $"{type}:third ! =",
            StringComparison.Ordinal));
        Assert.Contains(pureDestination.Diagnostics.Errors, d => d.Code == DiagnosticCode.EffectRowUnknown);
        Assert.False(Compile(source, UnknownCallPolicy.Permissive).HasErrors);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ! unknown")]
    public void MethodGroupsCannotEscapeIntoUnknownDestinations(string suffix)
    {
        var result = Compile($$"""
            module Contracts
              func Loud () → int
                ! cw
                Console.WriteLine("loud")
                return 1
              func Ignore (Func<int>:callback{{ExplicitContract(suffix)}}) → void
                !
                return
              func Run () → void
                !
                Ignore(Loud)
            """);
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.EffectRowUnknown);
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ! unknown")]
    public void RecursiveInferenceDoesNotTurnUnknownIntoPure(string suffix)
    {
        var result = Compile($$"""
            module Contracts
              func First private (Func<int>:callback{{ExplicitContract(suffix)}}) → int
                return Second(callback)
              func Second private (Func<int>:callback{{ExplicitContract(suffix)}}) → int
                let int:ignored = First(callback)
                return callback()
              func Run (Func<int>:callback{{ExplicitContract(suffix)}}) → int
                !
                return First(callback)
            """);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.EffectInferenceDidNotConverge);
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.ForbiddenEffect
            && d.Message.Contains("'Run'") && d.Message.Contains("'unknown'"));
    }

    [Theory]
    [InlineData("func Run (Action:callback ! unknown,cw) → void\n    !\n    return")]
    [InlineData("func Run (Action:callback ! UNKNOWN) → void\n    !\n    return")]
    [InlineData("func Run () → void\n    !\n    let Action:callback ! unknown = null")]
    [InlineData("func Run () → void\n    !\n    let callback = () ! unknown => 1")]
    [InlineData("class Holder\n    field Action:callback private ! unknown")]
    [InlineData("class Holder\n    property Action:Callback ! unknown\n      get\n      set")]
    public void UnknownCannotReplaceAnOwnContractOrMixWithKnownCodes(string declaration)
    {
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer("module Contracts\n  " + declaration, diagnostics)
            .TokenizeAllForParser(), diagnostics).Parse();
        Assert.True(diagnostics.HasErrors);
    }

    // 0.30 (maintainer decision 2026-11-14): a callable may declare ! unknown on itself.
    [Theory]
    [InlineData("func Run public (string?:s) → string?")]
    [InlineData("func Run private (string?:s) → string?")]
    public void UnknownIsDeclarableOnACallablesOwnRow(string signature)
    {
        var result = Compile($$"""
            module Contracts
              {{signature}}
                ! unknown
                return System.Web.HttpUtility.UrlEncode(s)
            """);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownExternalCall);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.EffectRowMisplaced);
    }

    [Fact]
    public void UnknownIsDeclarableOnAMethodAndPropagatesToUnknownCallers()
    {
        var result = Compile("""
            module Contracts
              class Encoder public
                func Encode public (string?:s) → string?
                  ! unknown
                  return System.Web.HttpUtility.UrlEncode(s)

                func Twice public (string?:s) → string?
                  ! unknown
                  return Encode(Encode(s))
            """);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
    }

    [Theory]
    [InlineData("!")]
    [InlineData("! cw")]
    public void ACallerWithKnownEffectsCannotCallAnUnknownDeclaration(string callerRow)
    {
        var result = Compile($$"""
            module Contracts
              func Encode public (string?:s) → string?
                ! unknown
                return System.Web.HttpUtility.UrlEncode(s)

              func Caller public (string?:s) → string?
                {{callerRow}}
                return Encode(s)
            """);
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.ForbiddenEffect
            && d.Message.Contains("'Caller'") && d.Message.Contains("'unknown'"));
    }

    [Fact]
    public void AnUndeclaredUnknownCallIsStillRejected()
    {
        var result = Compile("""
            module Contracts
              func Encode public (string?:s) → string?
                !
                return System.Web.HttpUtility.UrlEncode(s)
            """);
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.ForbiddenEffect
            && d.Message.Contains("declare ! unknown"));
    }

    [Fact]
    public void UnknownDoesNotMakeScalarRowsLegal()
    {
        var result = Compile("module Contracts\n  func Keep (int:value ! unknown) → int\n    !\n    return value");
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.EffectRowMisplaced);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ! unknown")]
    public void ConverterRetainsNativeMethodsWithoutInventingPureCallbackContracts(string suffix)
    {
        var conversion = new CSharpToCalorConverter().Convert("""
            using System;
            public delegate Func<int> Factory(Action callback);
            public static class Example
            {
                public static Func<int> Keep(Func<int> callback) { return callback; }
                public static int Run(Func<int> callback) { return callback(); }
            }
            """);
        Assert.True(conversion.Success);
        Assert.Equal(0, conversion.InteropPreservationCount);
        var ast = Parse(conversion.CalorSource!);
        Assert.True(Assert.Single(ast.Delegates).Effects!.IsUnknown);
        var methods = Assert.Single(ast.Classes).Methods;
        Assert.Equal(2, methods.Count);
        Assert.All(methods, method =>
        {
            var row = Assert.Single(method.Parameters).Row!;
            Assert.True(row.IsUnknown || row.EffectVariables.Count > 0);
        });
        Assert.True(methods.Single(method => method.Name == "Keep").Output!.Row!.IsUnknown);
        // The converter writes Run row-polymorphic (`<! callback0>`), so the ""
        // row compiles Run cleanly. The " ! unknown" row replaces that with an
        // explicit Unknown callback row on a pure Run: invoking it is charged
        // Unknown (Calor0425 at the invocation) and the pure row is rejected
        // (Calor0410). In both rows Keep's explicit `! unknown` return is
        // Calor0425, an error under --strict-effects (callback-row policy D2).
        const string polymorphicRun = """
                func Run <! callback0> (Func<int>:callback ! callback0) → int
                  ! callback0
            """;
        const string unknownRun = """
                func Run (Func<int>:callback ! unknown) → int
                  !
            """;
        var converted = conversion.CalorSource!.ReplaceLineEndings("\n");
        Assert.Contains(polymorphicRun.ReplaceLineEndings("\n"), converted, StringComparison.Ordinal);
        var source = suffix.Length == 0 ? converted
            : converted.Replace(polymorphicRun.ReplaceLineEndings("\n"), unknownRun.ReplaceLineEndings("\n"),
                StringComparison.Ordinal);
        var result = Compile(source);
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.EffectRowUnknown
            && d.Message.Contains("The return of 'Keep' explicitly declares Unknown effects", StringComparison.Ordinal));
        if (suffix.Length == 0)
        {
            Assert.DoesNotContain(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.ForbiddenEffect);
            Assert.Single(result.Diagnostics.Errors);
        }
        else
        {
            Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.ForbiddenEffect
                && d.Message.Contains("'Run' uses effect 'unknown'", StringComparison.Ordinal));
            Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.EffectRowUnknown
                && d.Message.Contains("Invocation of 'callback' in 'Run'", StringComparison.Ordinal));
        }
    }

    private static string ExplicitContract(string suffix) => suffix.Length == 0 ? " ! unknown" : suffix;

    private static CompilationResult Compile(string source, UnknownCallPolicy policy = UnknownCallPolicy.Strict) =>
        Program.Compile(source, "contracts.calr", new CompilationOptions
        {
            UnknownCallPolicy = policy,
            StrictEffects = true,
            VerifyContracts = false,
            StatusWriter = TextWriter.Null
        });

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var ast = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return ast;
    }
}
