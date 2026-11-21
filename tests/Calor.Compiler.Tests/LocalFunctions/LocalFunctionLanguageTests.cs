using System.Reflection;
using System.Runtime.Loader;
using Calor.Compiler.Ast;
using Calor.Compiler.Analysis;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.Migration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #1600: binding/lambda replacements retain local resolution, effect, capture,
/// verification and round-trip controls. Nested func declarations fail explicitly.
/// Conversion from C# is covered separately in LocalFunctionConversionTests.
/// </summary>
public class LocalFunctionLanguageTests
{
    private const string ShadowingModule = """
        module Shadow
          module Probe
            func Add public (int:x) → int
              ! cw
              System.Console.WriteLine("noisy")
              return x + 1000

            func Quiet public () → int
              !
              let Func<int,int>:Add = static (int:x) =>
                return x + 1
              return Add(2)

            func Run public () → string
              !
              return "" + Quiet()
        """;

    [Fact]
    public void NativeLocal_ShadowsSameNamedMember_ForResolutionAndEffects()
    {
        // The class Add prints; the local Add is pure. Quiet and Run declare !:
        // Charging the class method instead of the lambda would be Calor0410.
        var compilation = CompileNative(ShadowingModule, "shadow.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
        Assert.Equal("3", Run(compilation.GeneratedCode));
    }

    [Theory]
    [InlineData("let Func<int,int>:Help = static (int:x) =>\n        System.Console.WriteLine(\"hidden\")\n        return x\n      return Help(1)", "Calor0410")]
    [InlineData("let Func<int,int>:Help = static (int:x) =>\n        return x\n      return Help(1)", null)]
    [InlineData("let Func<int,int>:Help = static (int:x) =>\n        System.Console.WriteLine(\"deferred\")\n        return x\n      return 1", "Calor0410")]
    public void NativeLocal_LambdaBodyEffectsAreChargedToTheEnclosingCallable(string body, string? expectedCode)
    {
        var source = "module E\n  module Probe\n    func Get public () → int\n      !\n      "
            + body + "\n";
        var compilation = CompileNative(source, "effects.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        if (expectedCode == null)
            Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
        else
            Assert.Contains(compilation.Diagnostics.Errors, d => d.Code == expectedCode);
    }

    [Fact]
    public void NativeLocal_ShadowsModuleFunction_ForTypeChecking()
    {
        // The module Add takes and returns string; the local Add uses int.
        // The type checker must see the local signature, or y + 1 is a type error.
        var source = """
            module TypeShadow
              func Add public (string:x) → string
                !
                return x + "!"

              func Use public () → int
                !
                let Func<int,int>:Add = static (int:x) =>
                  return x + 1
                let y = Add(2)
                return y + 1
            """;
        var compilation = CompileNative(source, "typeshadow.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
        var binding = Assert.IsType<BindStatementNode>(compilation.Ast!.Functions.Single(f => f.Name == "Use").Body[0]);
        Assert.Equal("Add", binding.Name);
        Assert.True(Assert.IsType<LambdaExpressionNode>(binding.Initializer).IsStatic);
    }

    [Fact]
    public void NativeLocal_EscapingDelegate_CarriesTheLocalBodyRow()
    {
        var source = """
            module Escape
              module Probe
                func Help public (int:x) → int
                  !
                  return x

                func Get public () → Func<int,int> !
                  ! cw
                  let Func<int,int>:Help = static (int:x) =>
                    System.Console.WriteLine("hidden")
                    return x
                  return Help
            """;
        var compilation = CompileNative(source, "escape.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        // The pure class Help must not stand in for the printing local Help.
        Assert.Contains(compilation.Diagnostics.Errors, d => d.Code == "Calor0424");
    }

    [Fact]
    public void NativeLocal_DelegateInvokedInPlace_IsNotUnknown()
    {
        var source = """
            module Del
              module Probe
                func Run public () → string
                  ! cw
                  let Func<int,int>:Later = static (int:y) =>
                    System.Console.WriteLine("called")
                    return y * 10
                  let Func<int,int>:f = Later
                  return "" + f(4)
            """;
        var compilation = CompileNative(source, "del.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
        Assert.Equal("40", Run(compilation.GeneratedCode));
    }

    [Theory]
    [InlineData("if n > 0\n      func L (int:x) → int\n        return x\n      return L(n)\n    return 0", "conditional placement", 1)]
    [InlineData("func L (int:x) → int\n      func N (int:y) → int\n        return y\n      return N(x)\n    return L(n)", "nested placement", 2)]
    [InlineData("func L (int:x) → int\n      !\n      return x\n    return L(n)", "effect row", 1)]
    [InlineData("func L (int:x | (# > 0)) → int\n      return x\n    return L(n)", "contracts", 1)]
    [InlineData("func L <T> (T:x) → T\n      return x\n    return L(n)", "generic", 1)]
    [InlineData("func L (int:x) → System.Collections.Generic.IEnumerable<int>\n      yield x\n    return 0", "iterator", 1)]
    public void RetiredLocal_OutsideTheShape_IsRejectedWithLambdaGuidance(string body, string message, int expectedCount)
    {
        var source = "module Bad\n  func Outer public (int:n) → int\n    !\n    " + body + "\n";
        var compilation = Program.Compile(source, "bad.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.True(compilation.HasErrors, message);
        AssertRetiredLocal(compilation, expectedCount);
    }

    [Fact]
    public void NativeLocal_Capture_IsRejectedEvenWhenAFieldHasTheSameName()
    {
        // `n` is the outer parameter in C#, not the field: the static lambda
        // makes that a compile error rather than a silent rebind to the field.
        var source = """
            module Cap
              module Probe
                field int:n private = 5

                func Get public (int:n) → int
                  !
                  let Func<int,int>:AddN = static (int:x) =>
                    return x + n
                  return AddN(1)
            """;
        var compilation = CompileNative(source, "cap.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null, EnforceEffects = false });
        Assert.Contains(compilation.Diagnostics.Errors,
            d => d.Code == "Calor1002" && d.Message.Contains("CS8820"));
    }

    [Theory]
    [InlineData(6, true)]   // a false postcondition: the runtime guard must fire
    [InlineData(5, false)]  // a true one: still not proven from an omitted body
    public void NativeLocal_Verification_ClaimsNoProofFromTheLocalBody(int returned, bool throws)
    {
        var source = $$"""
            module Ver
              module Probe
                func Five public () → int
                  !
                  => result == 5
                  let Func<int>:Value = static () =>
                    return {{returned}}
                  return Value()

                func Run public () → string
                  !
                  return "" + Five()
            """;
        var compilation = CompileNative(source, "ver.calr", new CompilationOptions
        {
            StatusWriter = TextWriter.Null,
            VerifyContracts = true,
            ElideProvenGuards = true
        });
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
        Assert.Contains("ContractViolationException", compilation.GeneratedCode);
        Assert.DoesNotContain("// PROVEN", compilation.GeneratedCode);
        if (throws)
            Assert.ThrowsAny<Exception>(() => Run(compilation.GeneratedCode));
        else
            Assert.Equal("5", Run(compilation.GeneratedCode));
    }

    // ---- Codex review round 1 regressions ----

    [Fact]
    public void Review1_LocalShadowingAModuleFunction_IsNotQualifiedAway()
    {
        // The module Add prints and adds 1000; the call and the method group must
        // stay on the local Add (emission once qualified both to the module Add).
        var source = """
            module Shadow
              func Add public (int:x) → int
                ! cw
                System.Console.WriteLine("wrong")
                return x + 1000
              module Probe
                func Run public () → string
                  !
                  let Func<int,int>:Add = static (int:x) =>
                    return x + 1
                  let Func<int,int>:g = Add
                  return "" + Add(2) + g(0)
            """;
        var compilation = CompileNative(source, "r1a.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
        Assert.DoesNotContain("GlobalModule.Add(", compilation.GeneratedCode);
        Assert.Equal("31", Run(compilation.GeneratedCode));
    }

    [Fact]
    public void Review1_CaptureOfAnEnclosingName_IsNeverQualifiedToAModuleFunction()
    {
        var source = """
            module Cap
              func Add public (int:x) → int
                !
                return x + 1000
              module Probe
                func Get public (Func<int,int>:Add !) → int
                  ! cw
                  let Func<int>:L = static () =>
                    return Add(1)
                  return L()
            """;
        var compilation = CompileNative(source, "r1b.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null, EnforceEffects = false });
        Assert.Contains(compilation.Diagnostics.Errors,
            d => d.Code == "Calor1002" && d.Message.Contains("CS8820"));
    }

    [Theory]
    [InlineData("return (int:x) ! => Help(x)")]
    [InlineData("return (int:x) => Help(x)")]
    public void Review1_LambdaUsingALocalFunction_CarriesItsEffects(string returned)
    {
        var source = "module Escape\n  module Probe\n"
            + "    func Get public () → Func<int,int> !\n      ! cw\n"
            + "      let Func<int,int>:Help = static (int:x) =>\n        System.Console.WriteLine(\"hidden\")\n        return x\n"
            + "      " + returned + "\n";
        var compilation = CompileNative(source, "r2.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(compilation.Diagnostics.Errors,
            d => d.Code is "Calor0410" or "Calor0424");
    }

    [Fact]
    public void Review1_ASameNamedDelegateField_DoesNotShadowTheLocalFunctionRow()
    {
        var source = """
            module Escape
              module Probe
                field Func<int,int>:Help private !
                func Get public () → Func<int,int> !
                  ! cw
                  let Func<int,int>:Help = static (int:x) =>
                    System.Console.WriteLine("hidden")
                    return x
                  return Help
            """;
        var compilation = CompileNative(source, "r3.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(compilation.Diagnostics.Errors, d => d.Code == "Calor0424");
    }

    [Fact]
    public void Review1_AParameterShadowingASiblingLocal_IsInvokedAsAValue()
    {
        // Inside Other, Add is the delegate parameter (C#), not the class Add(str)
        // and not the sibling local Add: no Calor0208, and its row is Unknown.
        var source = """
            module Binder
              module Probe
                func Add public (string:x) → string
                  !
                  return x
                func M public () → int
                  !
                  let Func<int,int>:Add = static (int:x) =>
                    return x + 1
                  let Func<Func<int,int>,int>:Other = static (Func<int,int>:Add) =>
                    return Add(10)
                  return Other(Add)
            """;
        var compilation = CompileNative(source, "r4.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.DoesNotContain(compilation.Diagnostics.Errors, d => d.Code == "Calor0208");
        Assert.Contains(compilation.Diagnostics.Errors,
            d => d.Code == "Calor0410" && d.Message.Contains("unknown"));
    }

    [Fact]
    public void Review1_RetiredLocalFunctionInsideALambda_HasReplacementGuidance()
    {
        var source = """
            module BadPlacement
              func Make public () → Func<int,int> !
                !
                return (int:x) ! =>
                  func L (int:y) → int
                    return y
                  return x
            """;
        var compilation = Program.Compile(source, "r5.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        AssertRetiredLocal(compilation);
    }

    [Fact]
    public void Review1_RetiredEmbeddedGenericLocalName_HasReplacementGuidance()
    {
        var source = "module Bad\n  func Outer public () → int\n    func L<T> (int:x) → int\n      return x\n    return 0\n";
        var compilation = Program.Compile(source, "r7.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        AssertRetiredLocal(compilation);
    }

    // ---- Codex review round 2 regressions ----

    [Fact]
    public void Review2_AnAliasOfALocalFunction_IgnoresASameNamedField()
    {
        // `f` aliases the printing local Help, not the pure field Help; invoked in
        // an escaping lambda it must carry cw.
        var source = """
            module Escape
              module Probe
                field Func<int,int>:Help private !
                func Get public () → Func<int,int> !
                  ! cw
                  let Func<int,int>:Help = static (int:x) =>
                    System.Console.WriteLine("hidden")
                    return x
                  let Func<int,int>:f = Help
                  return (int:x) => f(x)
            """;
        var compilation = CompileNative(source, "r2a.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(compilation.Diagnostics.Errors,
            d => d.Code is "Calor0410" or "Calor0424" or "Calor0425");
    }

    [Fact]
    public void Review2_AnEnclosingPatternVariable_IsNeverQualifiedToAModuleFunction()
    {
        var source = """
            module Cap
              func Add public (int:x) → int
                !
                return x + 1000
              module Probe
                func Get public (object:o) → int
                  !
                  if o is Func<int,int> Add
                    return 1
                  let Func<int>:L = static () =>
                    return Add(1)
                  return L()
            """;
        var compilation = CompileNative(source, "r2b.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null, EnforceEffects = false });
        Assert.Contains(compilation.Diagnostics.Errors,
            d => d.Code == "Calor1002" && d.Message.Contains("CS8820"));
    }

    [Fact]
    public void Review2_ExpressionCallSpelling_IgnoresASameNamedField()
    {
        var source = """
            module Escape
              module Probe
                field Func<int,int>:Help private !
                func Get public () → Func<int,int> !
                  ! cw
                  let Func<int,int>:Help = static (int:x) =>
                    System.Console.WriteLine("hidden")
                    return x
                  return (int:x) => (Help)(x)
            """;
        var compilation = CompileNative(source, "r2c.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(compilation.Diagnostics.Errors, d => d.Code == "Calor0424");
    }

    [Fact]
    public void Review2_AConditionalReturningALocalFunction_CarriesItsRow()
    {
        var source = """
            module Escape
              module Probe
                func Get public (bool:c) → Func<int,int> !
                  ! cw
                  let Func<int,int>:Help = static (int:x) =>
                    System.Console.WriteLine("hidden")
                    return x
                  return c ? Help : Help
            """;
        var compilation = CompileNative(source, "r2d.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(compilation.Diagnostics.Errors, d => d.Code == "Calor0424");
    }

    // ---- Codex review round 3 regressions ----

    [Theory]
    // An expression-call argument, a null-coalescing operand, a qualified store:
    // These formerly unchecked local-function positions must not re-enable func.
    [InlineData("return (int:x) => Take(Help)")]
    [InlineData("return f ?? Help")]
    [InlineData("set Probe.Saved Help\n      return f")]
    public void Review3_RetiredLocalValueInUncheckedPositions_HasReplacementGuidance(string body)
    {
        var source = "module Escape\n  module Probe\n"
            + "    field Func<int,int>:Saved public !\n"
            + "    func Take public (Func<int,int>:g !) → int\n      !\n      return g(1)\n"
            + "    func Get public (Func<int,int>:f) → Func<int,int> !\n      ! cw,mut\n      "
            + body + "\n      func Help (int:x) → int\n        !\n        System.Console.WriteLine(\"hidden\")\n        return x\n";
        var compilation = Program.Compile(source, "r3.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        AssertRetiredLocal(compilation);
    }

    [Theory]
    [InlineData("Add", false)]
    [InlineData("Add.Invoke", true)]
    public void Review3_BindValidation_DoesNotUseAShadowedModuleSignature(string target, bool unknownInvocation)
    {
        var source = $$"""
            module Shadow
              using System.Collections.Generic
              func Add public (List<int>:xs) → int[]
                ! alloc
                return new int[0]
              func Use public (List<int>:xs) → List<int>
                !
                let Func<List<int>,List<int>>:Add = static (List<int>:xs) =>
                  return xs
                let List<int>:y = {{target}}(xs)
                return y
            """;
        var compilation = CompileNative(source, "r3e.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.DoesNotContain(compilation.Diagnostics.Errors, d => d.Code == "Calor0254");
        AssertBindingOutcome(compilation, unknownInvocation);
    }

    [Theory]
    [InlineData("Pick", false)]
    [InlineData("Pick.Invoke", true)]
    public void LocalValue_ArrayArgumentsDoNotUseTheShadowedModuleSignature(string target, bool unknownInvocation)
    {
        var source = $$"""
            module ArgumentShadow
              using System.Collections.Generic
              func Pick public (List<int>:xs) → int
                !
                return 0
              func Use public () → int
                ! alloc
                let Func<int[],int>:Pick = static (int[]:xs) =>
                  return 1
                return {{target}}(new int[0])
            """;
        var compilation = CompileNative(source, "argument-shadow.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        AssertBindingOutcome(compilation, unknownInvocation);
    }

    [Fact]
    public void Review3_AnOutOfScopeEnclosingName_FailsLoudlyNotSilently()
    {
        // The retired form must fail before it can silently rebind an out-of-scope
        // enclosing name to the module function.
        var source = """
            module Shadow
              func Add public (int:x) → int
                return x + 1000
              module Probe
                func Get public () → int
                  for Add from 0 to 1 step 1
                    let int:t = Add
                  func L () → int
                    return Add(1)
                  return L()
            """;
        var compilation = Program.Compile(source, "r3d.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        AssertRetiredLocal(compilation);
    }

    [Fact]
    public void NativeLocal_RoundTripsThroughTheCalorEmitter()
    {
        var parsed = CompileNative(ShadowingModule, "rt.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(parsed.HasErrors);
        var emitted = new CalorEmitter().Emit(parsed.Ast!);
        var emittedAst = CalorSourceHelper.Parse(emitted, "roundtrip-local.calr");
        Assert.True(emittedAst.IsSuccess, string.Join("\n", emittedAst.Errors));
        var quiet = emittedAst.Ast!.Classes.Single(type => type.Name == "Probe").Methods
            .Single(method => method.Name == "Quiet");
        var local = Assert.IsType<BindStatementNode>(quiet.Body[0]);
        Assert.Equal("Add", local.Name);
        Assert.True(Assert.IsType<LambdaExpressionNode>(local.Initializer).IsStatic);
        var again = CompileNative(emitted, "rt2.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(again.HasErrors, string.Join("\n", again.Diagnostics.Errors) + "\n" + emitted);
        Assert.Equal("3", Run(again.GeneratedCode));
    }

    public static IEnumerable<object[]> DelegateInvokeShadowingCases()
    {
        foreach (var type in new[] { "Func<int,int>", "System.Func<int,int>", "Callback" })
        foreach (var moduleWrites in new[] { false, true })
        foreach (var row in new[] { " !", " ! cw", "", " ! unknown" })
            yield return [type, moduleWrites, row];
    }

    [Theory]
    [MemberData(nameof(DelegateInvokeShadowingCases))]
    public void NativeDelegateInvoke_ChargesItsParameterRowNotAShadowedFunction(
        string type, bool moduleWrites, string row)
    {
        var source = $$"""
            module DelegateShadowing
              using Callback = System.Func<int,int>
              func Add (int:value) → int
                !{{(moduleWrites ? " cw" : "")}}
                {{(moduleWrites ? "System.Console.WriteLine(\"wrong\")" : "")}}
                return value + 1000
              func Use public ({{type}}:Add{{(row.Length == 0 ? " ! unknown" : row)}}, int:value) → int
                !
                return Add.Invoke(value)
            """;
        var parsed = CalorSourceHelper.Parse(source, "delegate-shadowing.calr");
        Assert.True(parsed.IsSuccess, string.Join("\n", parsed.Errors));
        var use = parsed.Ast!.Functions.Single(function => function.Name == "Use");
        var call = Assert.IsType<ExpressionCallNode>(
            Assert.IsType<ReturnStatementNode>(Assert.Single(use.Body)).Expression);
        var graph = CallGraphAnalysis.Build(parsed.Ast);
        var bound = graph.ComputedMemberCall(use.Id, call.Span);
        Assert.NotNull(bound);
        Assert.Equal(MethodKind.DelegateInvoke, bound.ResolvedMetadataMethod?.MethodKind);
        Assert.Empty(graph.ResolveCallSites(use.Id, "<expression-call>", call.Span));

        var compiled = CompileNative(source, "delegate-shadowing.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        var pure = row == " !";
        Assert.Equal(!pure, compiled.HasErrors);
        if (!pure)
        {
            Assert.Contains(compiled.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains(row == " ! cw" ? "'cw'" : "'unknown'", StringComparison.Ordinal));
            Assert.Equal(row != " ! cw", compiled.Diagnostics.Any(diagnostic =>
                diagnostic.Code == DiagnosticCode.EffectRowUnknown));
            return;
        }

        var invocations = 0;
        Func<int, int> callback = value =>
        {
            invocations++;
            return value + 1;
        };
        var returned = RunInAssembly(compiled.GeneratedCode, assembly =>
            Assert.Single(assembly.GetTypes(), candidate => candidate.GetMethod("Use") is not null)
                .GetMethod("Use")!.Invoke(null, [callback, 41]));
        Assert.Equal(42, Assert.IsType<int>(returned));
        Assert.Equal(1, invocations);
    }

    [Theory]
    [InlineData("Callback", "Callback")]
    [InlineData("Func<T,TResult>", "Func<int,int>")]
    public void NativeInvoke_OnASourceClassStillChargesTheSelectedMethod(string declaration, string type)
    {
        var source = $$"""
            module NotADelegate
              class {{declaration}}
                func Invoke public (int:value) → int
                  ! cw
                  System.Console.WriteLine("method")
                  return value
              func Use public ({{type}}:Add, int:value) → int
                !
                return Add.Invoke(value)
            """;
        var parsed = CalorSourceHelper.Parse(source, "source-invoke.calr");
        Assert.True(parsed.IsSuccess, string.Join("\n", parsed.Errors));
        var use = parsed.Ast!.Functions.Single();
        var invoker = Assert.Single(parsed.Ast.Classes);
        var invoke = Assert.Single(invoker.Methods);
        var call = Assert.IsType<ExpressionCallNode>(
            Assert.IsType<ReturnStatementNode>(Assert.Single(use.Body)).Expression);
        var graph = CallGraphAnalysis.Build(parsed.Ast);
        Assert.Equal(invoker.Name + "." + invoke.Id,
            Assert.Single(graph.ResolveCallSites(use.Id, "<expression-call>", call.Span)));
        Assert.Null(graph.ComputedMemberCall(use.Id, call.Span)!.ResolvedMetadataMethod);
        var compiled = CompileNative(source, "source-invoke.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        var error = Assert.Single(compiled.Diagnostics.Errors);
        Assert.Equal(DiagnosticCode.ForbiddenEffect, error.Code);
        Assert.Equal("Function 'Use' uses effect 'cw' but does not declare it", error.Message);
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Theory]
    [InlineData("Add")]
    [InlineData("Add.Invoke")]
    public void NativeDelegateInvocation_RetainsArgumentEvaluationEffects(string target)
    {
        var compiled = CompileNative($$"""
            module ArgumentEffects
              func Argument () → int
                ! cw
                System.Console.WriteLine("argument")
                return 41
              func Use public (Func<int,int>:Add !) → int
                !
                return {{target}}(Argument())
            """, "argument-effects.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        var error = Assert.Single(compiled.Diagnostics.Errors);
        Assert.Equal(DiagnosticCode.ForbiddenEffect, error.Code);
        Assert.Equal("Function 'Use' uses effect 'cw' but does not declare it", error.Message);
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.EffectRowUnknown);
    }

    private static void AssertBindingOutcome(CompilationResult compilation, bool unknownInvocation)
    {
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
        if (!unknownInvocation)
            return;

        // Retain the conservative named-call SDK oracle alongside native Invoke.
        var parsed = compilation.Ast!;
        var use = parsed.Functions.Single(function => function.Name == "Use");
        var replacements = 0;
        CallExpressionNode Named(ExpressionCallNode call)
        {
            var member = Assert.IsType<FieldAccessNode>(call.TargetExpression);
            var receiver = Assert.IsType<ReferenceNode>(member.Target);
            Assert.Equal("Invoke", member.FieldName);
            replacements++;
            return call.CopyMetadataTo(new CallExpressionNode(call.Span, receiver.Name + ".Invoke",
                call.Arguments, call.ArgumentNames, call.ArgumentModifiers, call.TypeArguments));
        }
        var body = use.Body.Select(statement => statement switch
        {
            BindStatementNode { Initializer: ExpressionCallNode call } binding =>
                (StatementNode)binding.CopyMetadataTo(new BindStatementNode(binding.Span, binding.Name,
                    binding.TypeName, binding.IsMutable, Named(call), binding.Attributes,
                    binding.IdentifierSpan, binding.TypeNameSpan, binding.Row)),
            ReturnStatementNode { Expression: ExpressionCallNode call } returned =>
                returned.CopyMetadataTo(new ReturnStatementNode(returned.Span, Named(call))),
            _ => statement
        }).ToArray();
        Assert.Equal(1, replacements);
        var replacement = use.CopyMetadataTo(new FunctionNode(use.Span, use.Id, use.Name, use.Visibility,
            use.Parameters, use.Output, use.Effects, use.Preconditions, use.Postconditions, body, use.Attributes));
        var namedModule = parsed.With(update =>
        {
            update.Functions = parsed.Functions.Select(function => ReferenceEquals(function, use) ? replacement : function).ToArray();
            update.Items = parsed.Items.Select(item => ReferenceEquals(item, use) ? replacement : item).ToArray();
        });
        var diagnostics = new DiagnosticBag();
        new Calor.Compiler.Binding.Binder(diagnostics).Bind(namedModule);
        new Calor.Compiler.TypeChecking.TypeChecker(diagnostics).Check(namedModule);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        new EffectEnforcementPass(diagnostics).Enforce(namedModule);
        var error = Assert.Single(diagnostics.Errors);
        Assert.Equal("Calor0410", error.Code);
        Assert.Equal("Function 'Use' uses effect 'unknown' but does not declare it\n"
            + "  Resolve the unknown call through a source declaration or effect contract, "
            + "or declare ! unknown on this callable.", error.Message);
    }

    private static void AssertRetiredLocal(CompilationResult compilation, int expectedCount = 1)
    {
        var errors = compilation.Diagnostics.Errors.ToArray();
        Assert.True(expectedCount == errors.Length, string.Join("\n", errors.Select(error => error.ToString())));
        Assert.All(errors, error =>
        {
            Assert.Equal("Calor0100", error.Code);
            Assert.Equal("Local func declarations are removed. Use a let binding with a lambda instead.", error.Message);
        });
    }

    private static CompilationResult CompileNative(
        string source, string path, CompilationOptions options)
    {
        var parsed = CalorSourceHelper.Parse(source, path);
        Assert.True(parsed.IsSuccess, string.Join("\n", parsed.Errors) + "\n" + source);
        return Program.Compile(source, path, options);
    }

    /// <summary>Compiles C# in memory and returns <c>Probe.Run()</c>.</summary>
    private static string Run(string csharp) => RunInAssembly(csharp, assembly =>
    {
        var probe = assembly.GetTypes().Single(type => type.Name == "Probe");
        var run = probe.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
        return Assert.IsType<string>(run.Invoke(null, null));
    });

    private static T RunInAssembly<T>(string csharp, Func<Assembly, T> run)
    {
        var compilation = CSharpCompilation.Create("F3_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(csharp)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics) + "\n" + csharp);
        stream.Position = 0;
        var context = new AssemblyLoadContext(compilation.AssemblyName!, isCollectible: true);
        try
        {
            try
            {
                return run(context.LoadFromStream(stream));
            }
            catch (TargetInvocationException invocation) when (invocation.InnerException != null)
            {
                throw invocation.InnerException;
            }
        }
        finally
        {
            context.Unload();
        }
    }
}
