using System.Reflection;
using System.Runtime.Loader;
using Calor.Compiler.Analysis;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class CanonicalOperatorIndexerTests
{
    [Theory]
    [InlineData("! cw", false)]
    [InlineData("!", true)]
    [InlineData("", true)]
    public void OperatorRows_EnforceDeclaredEffectsWithoutWeakeningTheMissingHeaderContract(
        string row, bool invalid)
    {
        var source = $$"""
            module OperatorRows
              class Box
                op implicit (Box:value) → int
                  {{row}}
                  System.Console.WriteLine("operator")
                  return 1
            """;
        foreach (var candidate in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var compiled = Program.Compile(candidate);
            Assert.Equal(invalid, compiled.HasErrors);
            if (invalid)
                Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == (row.Length == 0
                    ? DiagnosticCode.AccessorEffectContractUnavailable : DiagnosticCode.ForbiddenEffect));
            else
            {
                var module = Parse(candidate);
                var diagnostics = new DiagnosticBag();
                var bound = new Calor.Compiler.Binding.Binder(diagnostics).Bind(module);
                var op = Assert.Single(bound.Functions);
                Assert.Equal(BoundMemberKind.OperatorOverload, op.MemberKind);
                Assert.Contains("io:console_write", op.DeclaredEffects);
                var pass = new EffectEnforcementPass(diagnostics);
                pass.Enforce(module);
                Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
                var fact = Assert.Single(pass.DeclarationFacts).Value;
                Assert.True(fact.HasDeclaration);
                Assert.Equal("io:console_write", fact.InferredRow.ToDisplayString());
            }
        }
    }

    [Theory]
    [InlineData("! alloc, throw", false)]
    [InlineData("! alloc", true)]
    [InlineData("! throw", true)]
    [InlineData("!", true)]
    [InlineData("", true)]
    public void OperatorRows_KeepAllocationAndThrowCharges(string row, bool invalid)
    {
        var source = $$"""
            module ThrowingOperator
              class Box
                op implicit (Box:value) → int
                  {{row}}
                  throw new System.InvalidOperationException("original")
            """;
        foreach (var candidate in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var compiled = Program.Compile(candidate);
            Assert.Equal(invalid, compiled.HasErrors);
            if (invalid)
                Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == (row.Length == 0
                    ? DiagnosticCode.AccessorEffectContractUnavailable : DiagnosticCode.ForbiddenEffect));
            else
                Assert.Equal(42, Execute(compiled.GeneratedCode, assembly =>
                {
                    var type = assembly.GetType("Box", throwOnError: true)!;
                    var exception = Assert.Throws<TargetInvocationException>(() =>
                        type.GetMethod("op_Implicit")!.Invoke(null, [Activator.CreateInstance(type)]));
                    Assert.Equal("original", Assert.IsType<InvalidOperationException>(exception.InnerException).Message);
                    return 42;
                }));
        }
    }

    [Theory]
    [InlineData("! cw", false)]
    [InlineData("!", true)]
    public void OperatorRows_StillChargeTheActualCaller(string callerRow, bool invalid)
    {
        var source = $$"""
            module OperatorCaller
              class Box
                op explicit (Box:value) → int
                  ! cw
                  System.Console.WriteLine("operator")
                  return 1
              func Run public (Box:value) → int
                {{callerRow}}
                return (int)value
            """;
        var compiled = Program.Compile(source);
        Assert.Equal(invalid, compiled.HasErrors);
        if (invalid)
            Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("Run", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("return value")]
    [InlineData("let int:converted = value\n    return converted")]
    [InlineData("var int:converted = 0\n    set converted value\n    return converted")]
    [InlineData("return new int[] { value }[0]")]
    [InlineData("return true ? value : 0")]
    [InlineData("return new Receiver() { Value = value }.Value")]
    [InlineData("return Take(value)")]
    [InlineData("return Take(value: value)")]
    [InlineData("return new Receiver(value).Value")]
    [InlineData("return Identity<int>(value)")]
    [InlineData("return TakeMany(value, value)")]
    [InlineData("return TakeOptional(value)")]
    [InlineData("return TakeNamed(other: 0, value: value)")]
    [InlineData("Take(value)\n    return 7")]
    [InlineData("return new Receiver(0).Take(value)")]
    [InlineData("let IReceiver:receiver = new Receiver(0)\n    return receiver.Take(value)")]
    public void OperatorRows_ChargeImplicitReceivingConversions(string body)
    {
        foreach (var row in new[] { "!", "! cw" })
        {
            var source = $$"""
                module ImplicitOperatorCaller
                  class Box
                    op implicit (Box:value) → int
                      ! cw
                      System.Console.WriteLine("operator")
                      return 7
                  interface IReceiver
                    func Take (int:value) → int
                      !
                  class Receiver
                    implements IReceiver
                    field int:Value public
                    ctor ()
                    ctor (int:value)
                      set this.Value value
                    func Take public (int:value) → int
                      !
                      return value
                  func Take public (int:value) → int
                    !
                    return value
                  func Identity<T> public (T:value) → T
                    !
                    return value
                  func TakeMany public (params int[]:values) → int
                    !
                    return values[0]
                  func TakeOptional public (int:value, int:other = 0) → int
                    !
                    return value + other
                  func TakeNamed public (int:value, int:other) → int
                    !
                    return value + other
                  func Run public (Box:value) → int
                    {{(row == "!" ? "! alloc, mut" : "! cw, alloc, mut")}}
                    {{body}}
                """;
            var compiled = Program.Compile(source);
            if (body.StartsWith("return Take(", StringComparison.Ordinal))
            {
                var proof = CallGraphAnalysis.ResolveOperatorCalls(Parse(source));
                Assert.True(proof.SelectedCalls.Values.Any(call => call.UsesUserDefinedConversion),
                    string.Join("\n", proof.SelectedCalls.Select(pair =>
                        $"{pair.Key}: {pair.Value.Declaration.GetType().Name}, conversion={pair.Value.UsesUserDefinedConversion}"))
                    + string.Join("\n", proof.Failures));
            }
            Assert.True((row == "!") == compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            if (row == "!")
                Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                    && diagnostic.Message.Contains("Run", StringComparison.Ordinal));
            else
                Assert.Equal(7, Execute(compiled.GeneratedCode, assembly =>
                {
                    var box = Activator.CreateInstance(assembly.GetType("Box", throwOnError: true)!);
                    return Convert.ToInt64(Assert.Single(assembly.GetTypes(), type => type.GetMethod("Run") != null)
                        .GetMethod("Run")!.Invoke(null, [box]));
                }));
        }
    }

    [Theory]
    [InlineData("value + 1", "int", "7")]
    [InlineData("value + 2147483648", "long", "8")]
    [InlineData("-value", "int", "9")]
    public void OperatorRows_ChargeOnlyTheSelectedOverload(string expression, string resultType, string expected)
    {
        var source = $$"""
            module SelectedOperatorCaller
              class Box
                op + (Box:value, int:other) → int
                  !
                  return 7
                op + (Box:value, long:other) → long
                  ! cw
                  System.Console.WriteLine("long")
                  return 8L
                op - (Box:value) → int
                  ! cw
                  System.Console.WriteLine("negate")
                  return 9
              func Run public (Box:value) → {{resultType}}
                !
                return {{expression}}
            """;
        var compiled = Program.Compile(source);
        Assert.True((expected != "7") == compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        if (expected == "7")
            Assert.Equal(7, Execute(compiled.GeneratedCode, assembly =>
            {
                var box = Activator.CreateInstance(assembly.GetType("Box", throwOnError: true)!);
                return Convert.ToInt64(Assert.Single(assembly.GetTypes(), type => type.GetMethod("Run") != null)
                    .GetMethod("Run")!.Invoke(null, [box]));
            }));
        else
        {
            Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
            var accepted = Program.Compile(
                source.Replace("    !\n    return", "    ! cw\n    return", StringComparison.Ordinal));
            Assert.False(accepted.HasErrors, string.Join("\n", accepted.Diagnostics));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OperatorRows_ReceivingConversionSelectsTheCallableBeforeChargingEffects(bool constructor)
    {
        var source = $$"""
                module SelectedReceivingConversion
                  class Box
                    op implicit (Box:value) → int
                      ! cw
                      System.Console.WriteLine("conversion")
                      return 7
                  class Receiver
                    field int:Value public
                    ctor (int:value)
                      set this.Value value
                    ctor (object:value)
                      set this.Value 99
                  func Take public (int:value) → int
                    !
                    return value
                  func Take public (object:value) → int
                    !
                    return 99
                  func Run public (Box:value) → int
                    ! cw, alloc, mut
                    return {{(constructor ? "new Receiver(value).Value" : "Take(value)")}}
                """;
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Assert.Equal(7, Execute(compiled.GeneratedCode, assembly =>
        {
            var box = Activator.CreateInstance(assembly.GetType("Box", throwOnError: true)!);
            return Convert.ToInt64(Assert.Single(assembly.GetTypes(), type => type.GetMethod("Run") != null)
                .GetMethod("Run")!.Invoke(null, [box]));
        }));
        var denied = Program.Compile(source.Replace("! cw, alloc, mut", "! alloc, mut", StringComparison.Ordinal));
        Assert.Contains(denied.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("Run", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("explicit", "value")]
    [InlineData("implicit", "ref value")]
    public void OperatorRows_InvalidArgumentConversionsRemainRejected(string kind, string argument)
    {
        var source = $$"""
                module InvalidArgumentConversion
                  class Box
                    op {{kind}} (Box:value) → int
                      !
                      return 7
                  func Take public ({{(argument.StartsWith("ref", StringComparison.Ordinal) ? "ref " : "")}}int:value) → int
                    !
                    return value
                  func Run public (Box:value) → int
                    !
                    return Take({{argument}})
                """;
        var compiled = Program.Compile(source);
        Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }

    [Theory]
    [InlineData("color.Take(value)", "0,1")]
    [InlineData("color.Take(value: value)", "0,1")]
    [InlineData("color.TakeOptional(value)", "0,1")]
    [InlineData("color.TakeNamed(other: 0, value: value)", "0,2,1")]
    [InlineData("color.TakeMany(value, value)", "0,1,1")]
    [InlineData("color.TakeGeneric<int>(other: 0, value: value)", "0,2,1")]
    public void OperatorRows_EnumExtensionReceivingConversionsRetainReceiverAndCallerCharges(
        string call, string parameterOrder)
    {
        var source = $$"""
            module ExtensionReceivingConversion
              enum Color
                Red
              class Box
                op implicit (Box:value) → int
                  ! cw
                  System.Console.WriteLine("conversion")
                  return 7
              module ColorExtensions
                func Take public (this Color:color, int:value) → int
                  !
                  return value
                func Take public (this Color:color, object:value) → int
                  !
                  return 99
                func TakeOptional public (this Color:color, int:value, int:other = 0) → int
                  !
                  return value + other
                func TakeNamed public (this Color:color, int:value, int:other) → int
                  !
                  return value + other
                func TakeMany public (this Color:color, params int[]:values) → int
                  !
                  return values[0]
                func TakeGeneric<T> public (this Color:color, int:value, T:other) → int
                  !
                  return value
              func Run public (Color:color, Box:value) → int
                ! cw, alloc
                return {{call}}
            """;
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var diagnostics = new DiagnosticBag();
        var bound = new Calor.Compiler.Binding.Binder(diagnostics).Bind(Parse(source));
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var selectedCall = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(
            bound.Functions.Single(function => function.Symbol.Name == "Run")).OfType<BoundCallExpression>());
        Assert.True(selectedCall.IsSourceExtensionCall);
        Assert.Equal(parameterOrder, string.Join(",",
            Assert.Single(selectedCall.SelectedOverloadMatches).Arguments.Select(argument => argument.ParameterIndex)));
        Assert.Equal(7, Execute(compiled.GeneratedCode, assembly =>
        {
            var box = Activator.CreateInstance(assembly.GetType("Box", throwOnError: true)!);
            var color = Enum.ToObject(assembly.GetType("Color", throwOnError: true)!, 0);
            return Convert.ToInt64(Assert.Single(assembly.GetTypes(), type => type.GetMethod("Run") != null)
                .GetMethod("Run")!.Invoke(null, [color, box]));
        }));
        var denied = Program.Compile(source.Replace("! cw, alloc", "! alloc", StringComparison.Ordinal));
        Assert.Contains(denied.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("Run", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("explicit", "value")]
    [InlineData("implicit", "ref value")]
    public void OperatorRows_EnumExtensionInvalidReceivingConversionsRemainRejected(string kind, string argument)
    {
        var compiled = Program.Compile($$"""
            module InvalidExtensionConversion
              enum Color
                Red
              class Box
                op {{kind}} (Box:value) → int
                  !
                  return 7
              module Extensions
                func Take (this Color:color, {{(argument.StartsWith("ref", StringComparison.Ordinal) ? "ref " : "")}}int:value) → int
                  !
                  return value
              func Run public (Color:color, Box:value) → int
                !
                return color.Take({{argument}})
            """);
        Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }

    [Fact]
    public void OperatorRows_AmbiguousEnumExtensionConversionsDoNotFallBackToObject()
    {
        var compiled = Program.Compile("""
            module AmbiguousExtensionConversion
              enum Color
                Red
              class Box
                op implicit (Box:value) → int
                  !
                  return 7
                op implicit (Box:value) → string
                  !
                  return "seven"
              module Extensions
                func Take (this Color:color, int:value) → int
                  !
                  return value
                func Take (this Color:color, string:value) → int
                  !
                  return 8
                func Take (this Color:color, object:value) → int
                  !
                  return 99
              func Run public (Color:color, Box:value) → int
                !
                return color.Take(value)
            """);
        Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.AmbiguousOverload);
    }

    [Fact]
    public void OperatorRows_EnumExtensionReceivingConversionsChargeTheSelectedCallee()
    {
        const string source = """
            module EffectfulExtensionConversion
              enum Color
                Red
              class Box
                op implicit (Box:value) → int
                  !
                  return 7
              module Extensions
                func Take (this Color:color, int:value) → int
                  ! cw
                  System.Console.WriteLine("callee")
                  return value
                func Take (this Color:color, object:value) → int
                  !
                  return 99
              func Run public (Color:color, Box:value) → int
                !
                return color.Take(value)
            """;
        var denied = Program.Compile(source);
        Assert.Contains(denied.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("Run", StringComparison.Ordinal));
        var compiled = Program.Compile(source.Replace("    !\n    return color", "    ! cw\n    return color", StringComparison.Ordinal));
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Assert.Equal(7, Execute(compiled.GeneratedCode, assembly =>
        {
            var box = Activator.CreateInstance(assembly.GetType("Box", throwOnError: true)!);
            var color = Enum.ToObject(assembly.GetType("Color", throwOnError: true)!, 0);
            return Convert.ToInt64(Assert.Single(assembly.GetTypes(), type => type.GetMethod("Run") != null)
                .GetMethod("Run")!.Invoke(null, [color, box]));
        }));
    }

    [Fact]
    public void OperatorRows_AmbiguousReceivingConversionsDoNotFallBackToObject()
    {
        var compiled = Program.Compile("""
                module AmbiguousArgumentConversion
                  class Box
                    op implicit (Box:value) → int
                      !
                      return 7
                    op implicit (Box:value) → string
                      !
                      return "seven"
                  func Take public (int:value) → int
                    !
                    return value
                  func Take public (string:value) → int
                    !
                    return 8
                  func Take public (object:value) → int
                    !
                    return 99
                  func Run public (Box:value) → int
                    !
                    return Take(value)
                """);
        Assert.True(compiled.HasErrors, compiled.GeneratedCode);
        Assert.Contains(compiled.Diagnostics, diagnostic =>
            diagnostic.Code is DiagnosticCode.AmbiguousOverload or DiagnosticCode.NoMatchingOverload);
    }

    [Fact]
    public void OperatorRows_SdkLocalFunctionsKeepReceivingConversionsAndCallerCharges()
    {
        const string source = """
            module LocalReceivingConversion
              class Box
                op implicit (Box:value) → int
                  ! cw
                  System.Console.WriteLine("conversion")
                  return 7
              func Take (int:value) → int
                !
                return value
              func Run public (Box:value) → int
                ! alloc, mut
                System.GC.KeepAlive(value)
                return Take(value)
            """;
        foreach (var candidate in new[] { source,
                     source.Replace("! alloc, mut", "! alloc, mut, cw", StringComparison.Ordinal) })
        {
            var parsed = Parse(candidate);
            var take = Assert.Single(parsed.Functions, function => function.Name == "Take");
            var run = Assert.Single(parsed.Functions, function => function.Name == "Run");
            var moved = parsed.With(module =>
            {
                module.Functions = [run];
                module.Items = parsed.Items.Where(item => !ReferenceEquals(item, take)).ToArray();
            });
            var module = ReplaceRunBody(moved,
            [
                new LocalFunctionStatementNode(run.Body[0].Span, take),
                run.Body[1]
            ]);
            var diagnostics = new DiagnosticBag();
            new Calor.Compiler.Binding.Binder(diagnostics).Bind(module);
            new EffectEnforcementPass(diagnostics).Enforce(module);
            if (candidate == source)
                Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                    && diagnostic.Message.Contains("Run", StringComparison.Ordinal));
            else
            {
                Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
                Assert.Equal(7, Execute(new Calor.Compiler.CodeGen.CSharpEmitter().Emit(module), assembly =>
                {
                    var box = Activator.CreateInstance(assembly.GetType("Box", throwOnError: true)!);
                    return Convert.ToInt64(Assert.Single(assembly.GetTypes(), type => type.GetMethod("Run") != null)
                        .GetMethod("Run")!.Invoke(null, [box]));
                }));
            }
        }
    }

    [Fact]
    public void OperatorRows_UnprovenArgumentOriginsAreNotPurityEvidence()
    {
        var parsed = Parse("""
            module SharedArgumentOrigin
              class Box
                op implicit (Box:value) → int
                  !
                  return 7
              func Take (int:value) → int
                ! cw
                System.Console.WriteLine("callee")
                return value
              func Take (object:value) → int
                !
                return 99
              func Run public (Box:value) → int
                !
                Take(value)
                return Take(value)
            """);
        var run = Assert.Single(parsed.Functions, function => function.Name == "Run");
        var first = Assert.IsType<ExpressionStatementNode>(run.Body[0]);
        var firstCall = Assert.IsType<CallExpressionNode>(first.Expression);
        var returned = Assert.IsType<ReturnStatementNode>(run.Body[1]);
        var secondCall = Assert.IsType<CallExpressionNode>(returned.Expression);
        var sharedCall = secondCall.CopyMetadataTo(new CallExpressionNode(secondCall.Span,
            secondCall.Target, firstCall.Arguments, secondCall.ArgumentNames, secondCall.ArgumentModifiers,
            secondCall.TypeArguments, secondCall.CalleeSpan, secondCall.ReceiverSpan));
        var module = parsed.With(update =>
        {
            var replacement = run.CopyMetadataTo(new FunctionNode(run.Span, run.Id, run.Name, run.Visibility,
                run.Parameters, run.Output, run.Effects, run.Preconditions, run.Postconditions,
                [first, new ReturnStatementNode(returned.Span, sharedCall)], run.Attributes));
            update.Functions = parsed.Functions.Select(function => ReferenceEquals(function, run) ? replacement : function).ToArray();
            update.Items = parsed.Items.Select(item => ReferenceEquals(item, run) ? replacement : item).ToArray();
        });
        var diagnostics = new DiagnosticBag();
        new Calor.Compiler.Binding.Binder(diagnostics).Bind(module);
        new EffectEnforcementPass(diagnostics).Enforce(module);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("no unique Calor argument origin", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OperatorRows_ConstructorChainingUsesTheSelectedReceivingConversion(bool effectful)
    {
        var compiled = Program.Compile($$"""
            module ChainedReceivingConversion
              class Box
                op implicit (Box:value) → int
                  {{(effectful ? "! cw" : "!")}}
                  {{(effectful ? "System.Console.WriteLine(\"conversion\")" : "")}}
                  return 7
              class Base
                field int:Value public
                ctor (int:value)
                  set this.Value value
              class Derived
                extends Base
                ctor (Box:value)
                  base(value)
              func Run public (Box:value) → int
                ! alloc, mut
                return new Derived(value).Value
            """);
        if (effectful)
        {
            Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ConstructorEffectContractUnavailable);
            Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("Run", StringComparison.Ordinal));
            return;
        }
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Assert.Equal(7, Execute(compiled.GeneratedCode, assembly =>
        {
            var box = Activator.CreateInstance(assembly.GetType("Box", throwOnError: true)!);
            return Convert.ToInt64(Assert.Single(assembly.GetTypes(), type => type.GetMethod("Run") != null)
                .GetMethod("Run")!.Invoke(null, [box]));
        }));
    }

    [Theory]
    [InlineData("&&", "&")]
    [InlineData("||", "|")]
    public void OperatorRows_ChargeShortCircuitTruthOperators(string logical, string bitwise)
    {
        var source = $$"""
            module TruthOperatorCaller
              class Box
                op true (Box:value) → bool
                  ! cw
                  System.Console.WriteLine("true")
                  return true
                op false (Box:value) → bool
                  ! cw
                  System.Console.WriteLine("false")
                  return false
                op {{bitwise}} (Box:left, Box:right) → Box
                  !
                  return left
              func Run public (Box:left, Box:right) → Box
                !
                return left {{logical}} right
            """;
        var denied = Program.Compile(source);
        Assert.Contains(denied.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("Run", StringComparison.Ordinal));
        var accepted = Program.Compile(
            source.Replace("    !\n    return left", "    ! cw\n    return left", StringComparison.Ordinal));
        Assert.False(accepted.HasErrors, string.Join("\n", accepted.Diagnostics));
    }

    [Theory]
    [InlineData("FEATURE")]
    [InlineData("!FEATURE")]
    public void OperatorRows_ChargeConversionsInEveryConditionalBranch(string condition)
    {
        var source = $$"""
            module ConditionalOperatorCaller
              class Box
                op explicit (Box:value) → int
                  ! cw
                  System.Console.WriteLine("operator")
                  return 7
              class Calls
                pp {{condition}}
                  func Run public static (Box:value) → int
                    !
                    return (int)value
            """;
        var denied = Program.Compile(source);
        Assert.Contains(denied.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("Run", StringComparison.Ordinal));
        var accepted = Program.Compile(
            source.Replace("        !\n", "        ! cw\n", StringComparison.Ordinal));
        Assert.False(accepted.HasErrors, string.Join("\n", accepted.Diagnostics));
    }

    [Fact]
    public void OperatorRows_ConditionalDeclarationsRetainBothSelectedIdentities()
    {
        var source = """
            module ConditionalOperatorDeclarations
              class Box
                pp FEATURE
                  op explicit (Box:value) → int
                    ! cw
                    System.Console.WriteLine("feature")
                    return 7
                pp else
                  op explicit (Box:value) → int
                    ! cw
                    System.Console.WriteLine("other")
                    return 8
              func Run public (Box:value) → int
                !
                return (int)value
            """;
        var module = Parse(source);
        var graph = CallGraphAnalysis.Build(module);
        var run = Assert.Single(module.Functions);
        var cast = Assert.IsType<ReturnStatementNode>(Assert.Single(run.Body)).Expression!;
        var expected = CallGraphAnalysis.EnumerateOperators(Assert.Single(module.Classes))
            .Select(op => $"Box.{op.Id}").Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(2, expected.Length);
        Assert.Equal(expected, graph.OperatorCalls(cast).Order(StringComparer.Ordinal));
        var denied = Program.Compile(source);
        Assert.Contains(denied.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
        var accepted = Program.Compile(
            source.Replace("    !\n    return", "    ! cw\n    return", StringComparison.Ordinal));
        Assert.False(accepted.HasErrors, string.Join("\n", accepted.Diagnostics));
    }

    [Fact]
    public void OperatorRows_UnexhaustedConditionalAnalysisFailsExplicitly()
    {
        var compiled = Program.Compile("""
            module ConditionalOperatorBound
              class Box
                op explicit (Box:value) → int
                  !
                  return 7
              class Calls
                pp A || B || C || D || E || F || G || H || I
                  func Run public static (Box:value) → int
                    !
                    return (int)value
            """);
        Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("preprocessor condition symbols", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(UnaryOperator.PostIncrement)]
    [InlineData(UnaryOperator.PreIncrement)]
    public void OperatorRows_ChargeSdkIncrementNodes(UnaryOperator update)
    {
        const string source = """
            module IncrementOperatorCaller
              class Box
                op ++ (Box:value) → Box
                  ! cw
                  System.Console.WriteLine("increment")
                  return value
              func Run public (Box:value) → Box
                !
                System.GC.KeepAlive(value)
                return value
            """;
        foreach (var candidate in new[] { source, source.Replace("    !\n", "    ! cw, mut\n", StringComparison.Ordinal) })
        {
            var parsed = Parse(candidate);
            var run = Assert.Single(parsed.Functions);
            var returned = Assert.IsType<ReturnStatementNode>(run.Body[1]);
            var updateSpan = run.Body[0].Span;
            var body = new StatementNode[]
            {
                new ExpressionStatementNode(updateSpan,
                    new UnaryOperationNode(updateSpan, update, new ReferenceNode(updateSpan, "value"))),
                returned
            };
            var module = ReplaceRunBody(parsed, body);
            var diagnostics = new DiagnosticBag();
            _ = new Calor.Compiler.Binding.Binder(diagnostics).Bind(module);
            new EffectEnforcementPass(diagnostics).Enforce(module);
            if (candidate == source)
                Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                    && diagnostic.Message.Contains("Run", StringComparison.Ordinal));
            else
                Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OperatorRows_ChargeAssignmentOutputConversions(bool sdkCompound)
    {
        var source = """
            module CompoundOperatorCaller
              class Box
                op + (Box:value, int:other) → Result
                  ! alloc
                  return new Result()
              class Result
                op implicit (Result:value) → Box
                  ! cw
                  System.Console.WriteLine("output")
                  return null
              func Run public (Box:value) → Box
                ! alloc, mut
                set value value + 1
                return value
            """;
        foreach (var candidate in new[] { source,
                     source.Replace("    ! alloc, mut", "    ! alloc, mut, cw", StringComparison.Ordinal) })
        {
            var module = Parse(candidate);
            if (sdkCompound)
            {
                var run = Assert.Single(module.Functions);
                var assignment = Assert.IsType<AssignmentStatementNode>(run.Body[0]);
                var addition = Assert.IsType<BinaryOperationNode>(assignment.Value);
                module = ReplaceRunBody(module,
                [
                    new CompoundAssignmentStatementNode(assignment.Span, assignment.Target,
                        CompoundAssignmentOperator.Add, addition.Right),
                    run.Body[1]
                ]);
            }
            var diagnostics = new DiagnosticBag();
            new EffectEnforcementPass(diagnostics).Enforce(module);
            if (candidate == source)
                Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                    && diagnostic.Message.Contains("Run", StringComparison.Ordinal));
            else
                Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        }
    }

    private static ModuleNode ReplaceRunBody(ModuleNode parsed, IReadOnlyList<StatementNode> body)
    {
        var run = Assert.Single(parsed.Functions);
        var function = run.CopyMetadataTo(new FunctionNode(run.Span, run.Id, run.Name, run.Visibility, run.Parameters,
            run.Output, run.Effects, run.Preconditions, run.Postconditions, body, run.Attributes));
        return parsed.With(module =>
        {
            module.Functions = [function];
            module.Items = parsed.Items.Select(item => ReferenceEquals(item, run) ? function : item).ToArray();
        });
    }

    [Theory]
    [InlineData("return 1\n      ! cw")]
    [InlineData("! cw\n      ! alloc\n      return 1")]
    public void OperatorRows_RejectLateAndDuplicateHeaders(string body)
    {
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer($"module InvalidRows\n  class Box\n    op implicit (Box:value) → int\n      {body}",
            diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnexpectedToken);
    }

    [Fact]
    public void OperatorRows_DoNotAuthorizeUnknownCalls()
    {
        const string invalidConversion = """
            module UnknownOperator
              class Box
                op implicit (Box:value) → object
                  ! alloc, mut, cw, throw
                  return System.Globalization.CultureInfo.InvariantCulture
            """;
        _ = Parse(invalidConversion);
        var invalid = Program.Compile(invalidConversion);
        Assert.True(invalid.HasErrors);
        Assert.Contains(invalid.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.CodeGenCompilationError
            && diagnostic.Message.Contains("CS0553", StringComparison.Ordinal));
        Assert.DoesNotContain(invalid.Diagnostics, diagnostic => diagnostic.Code is
            DiagnosticCode.UnknownExternalCall or DiagnosticCode.ForbiddenEffect);

        const string source = """
            module UnknownOperator
              class Box
                op implicit (Box:value) → System.Globalization.CultureInfo
                  ! alloc, mut, cw, throw
                  return System.Globalization.CultureInfo.CurrentCulture
            """;
        foreach (var candidate in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var parsed = Parse(candidate);
            var compiled = Program.Compile(candidate);
            Assert.True(compiled.HasErrors);
            Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall
                && diagnostic.Message.Contains("get_CurrentCulture", StringComparison.Ordinal));
            Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
            Assert.DoesNotContain(compiled.Diagnostics,
                diagnostic => diagnostic.Code == DiagnosticCode.CodeGenCompilationError);
            Assert.Equal(1, Execute(new CSharpEmitter().Emit(parsed), assembly =>
            {
                var box = assembly.GetType("Box", throwOnError: true)!;
                var result = box.GetMethod("op_Implicit")!.Invoke(null, [Activator.CreateInstance(box)]);
                return ReferenceEquals(System.Globalization.CultureInfo.CurrentCulture, result) ? 1 : 0;
            }));
        }
    }

    [Fact]
    public void OperatorRows_ExplicitPureGetterContractRetainsItsClrResult()
    {
        const string source = """
            module KnownOperator
              class Box
                op implicit (Box:value) → System.Globalization.CultureInfo
                  !
                  return System.Globalization.CultureInfo.InvariantCulture
            """;
        foreach (var candidate in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            _ = Parse(candidate);
            var compiled = Program.Compile(candidate);
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            Assert.Equal(1, Execute(compiled.GeneratedCode, assembly =>
            {
                var box = assembly.GetType("Box", throwOnError: true)!;
                var result = box.GetMethod("op_Implicit")!.Invoke(null, [Activator.CreateInstance(box)]);
                return ReferenceEquals(System.Globalization.CultureInfo.InvariantCulture, result) ? 1 : 0;
            }));
        }
    }

    [Theory]
    [InlineData("System.DateTime", "System.TimeSpan", true)]
    [InlineData("System.Data.SqlTypes.SqlInt32", "System.Data.SqlTypes.SqlInt32", false)]
    public void OperatorRows_MetadataOperatorsRequireExistingContracts(string leftType, string rightType, bool known)
    {
        var source = $$"""
            module MetadataOperatorBody
              class Box
                field {{leftType}}:Left public
                field {{rightType}}:Right public
                op implicit (Box:value) → {{leftType}}
                  ! alloc, mut, cw, throw
                  return value.Left + value.Right
            """;
        var compiled = Program.Compile(source);
        var module = Parse(source);
        var op = Assert.Single(Assert.Single(module.Classes).OperatorOverloads);
        var expression = Assert.IsType<ReturnStatementNode>(Assert.Single(op.Body)).Expression!;
        var key = Assert.Single(CallGraphAnalysis.Build(module).ExternalOperatorCalls(expression));
        Assert.False(key.FromStringFallback);
        Assert.Equal("op_Addition", key.MemberName);
        Assert.Equal(!known, new EffectResolver().Resolve(key).Status == EffectResolutionStatus.Unknown);
        if (known)
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        else
        {
            Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall
                && diagnostic.Message.Contains("op_Addition", StringComparison.Ordinal));
            Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
        }
    }

    [Fact]
    public void OperatorRows_BuiltinDecimalOperationsDoNotInventMetadataCalls()
    {
        var compiled = Program.Compile("""
            module DecimalOperatorBody
              class Box
                op implicit (Box:value) → decimal
                  !
                  return 1m + 2m
            """);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
    }

    [Fact]
    public void OperatorRows_SynthesisDoesNotInventHeadersForUserDeclarations()
    {
        var module = Parse("""
            module MissingOperatorHeader
              class Box
                op implicit (Box:value) → int
                  System.Console.WriteLine("operator")
                  return 1
            """);
        Assert.True(EffectEnforcementPass.SynthesizeDeclaredRows(module));
        Assert.Null(Assert.Single(module.Classes.Single().OperatorOverloads).Effects);
        var diagnostics = new DiagnosticBag();
        new EffectEnforcementPass(diagnostics).Enforce(module);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.AccessorEffectContractUnavailable);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OperatorRows_ConverterCorrelatesDistinctOverloadedRows(bool nested)
    {
        const string declaration = """
            public class Box
            {
                public static int operator +(Box value, int other)
                {
                    System.Console.WriteLine("original");
                    return other;
                }
                public static int operator +(Box value, long other)
                {
                    throw new System.InvalidOperationException("original");
                }
            }
            """;
        var original = nested ? "public class Outer {\n" + declaration + "\n}" : declaration;
        var converted = new CSharpToCalorConverter().Convert(original);
        Assert.True(converted.Success, string.Join("\n", converted.Issues));
        Assert.Empty(converted.Losses);
        RawFrameAssertions.AssertFramed(converted.CalorSource!, present: false);
        foreach (var candidate in new[] { converted.CalorSource!, new CalorEmitter().Emit(Parse(converted.CalorSource!)) })
        {
            var module = Parse(candidate);
            var operators = CallGraphAnalysis.EnumerateClasses(module).SelectMany(CallGraphAnalysis.EnumerateOperators)
                .ToArray();
            Assert.Equal(2, operators.Length);
            Assert.Contains("console_write", operators[0].Effects!.Effects["io"]);
            Assert.DoesNotContain("io", operators[1].Effects!.Effects.Keys);
            var compiled = Program.Compile(candidate);
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        }
    }

    private const string Money = """
        module Operators
          class Money
            property long:Cents
              get
            ctor (long:cents)
              set Cents cents
            op + (Money:a, Money:b) → Money
              return new Money(a.Cents + b.Cents)
            op implicit (long:cents) → Money
              return new Money(cents)
        """;

    private const string Bag = """
        module Indexers
          class Bag
            field int[]:_items private = new int[3]
            this[int:i] → int
              get
                return _items[i]
              set
                set _items[i] value
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CountedOperatorsExecuteOnTheClr(bool roundtrip)
    {
        var source = roundtrip ? new CalorEmitter().Emit(Parse(Money)) : Money;
        Assert.DoesNotContain('§', source);
        Assert.Contains("op + (Money:a, Money:b) → Money", source);
        var result = Program.Compile(source);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.Equal(42, Execute(result.GeneratedCode, AddMoney));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CountedIndexerLoadsAndStoresExecuteOnTheClr(bool roundtrip)
    {
        var source = roundtrip ? new CalorEmitter().Emit(Parse(Bag)) : Bag;
        Assert.DoesNotContain('§', source);
        Assert.Contains("this[int:i] → int", source);
        var result = Program.Compile(source);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.Equal(42, Execute(result.GeneratedCode, ReadAndWriteIndexer));
    }

    [Theory]
    [InlineData("public class Bag { private int[] _items = new int[3]; public int this[int i] { get { return _items[i]; } set { _items[i] = value; } } }")]
    [InlineData("public class Bag { private int[] _items = new int[3]; internal int this[int @step] { get { return _items[@step]; } set { _items[@step] = value; } } }")]
    [InlineData("public class Bag { private int[] _items = new int[3]; public int this[int i] { get { return _items[i]; } private set { _items[i] = value; } } }")]
    public void RealConverterPreservesIndexerVisibilityAndClrBehavior(string source)
    {
        var converted = new CSharpToCalorConverter().Convert(source);
        Assert.True(converted.Success, string.Join("\n", converted.Context.Issues));
        Assert.False(converted.CalorSource!.Contains("raw ", StringComparison.Ordinal),
            string.Join("\n", converted.Context.Issues.Select(issue => issue.Message)) + "\n" + converted.CalorSource);
        Assert.DoesNotContain('§', converted.CalorSource!);
        var original = Execute(source, ReadAndWriteIndexer);
        var result = Program.Compile(converted.CalorSource!);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.Equal(original, Execute(result.GeneratedCode, ReadAndWriteIndexer));
        var expected = source.Contains("internal int", StringComparison.Ordinal)
            ? Visibility.Internal : Visibility.Public;
        Assert.Equal(expected, Parse(converted.CalorSource!).Classes.Single().Indexers.Single().Visibility);
        if (source.Contains("private set", StringComparison.Ordinal))
            Assert.Equal(Visibility.Private, Parse(converted.CalorSource!).Classes.Single().Indexers.Single().Setter!.Visibility);
    }

    [Fact]
    public void ThisExpressionStatementsAreNotMistakenForIndexerDeclarations()
    {
        var source = Bag + """

                func Touch () → void
                  return
                func Exercise () → int
                  this.Touch()
                  return this[1]
            """;
        foreach (var input in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var result = Program.Compile(input);
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.Equal(0, Execute(result.GeneratedCode, assembly =>
            {
                var type = assembly.GetType("Bag", throwOnError: true)!;
                return (int)type.GetMethod("Exercise")!.Invoke(Activator.CreateInstance(type), null)!;
            }));
        }
    }

    [Fact]
    public void RealConverterRetainsOperatorsWithoutRawRescue()
    {
        const string source = """
            public class Money
            {
                public long Cents { get; }
                public Money(long cents) { Cents = cents; }
                public static Money operator +(Money a, Money b) { return new Money(a.Cents + b.Cents); }
                public static implicit operator Money(long cents) { return new Money(cents); }
            }
            """;
        var converted = new CSharpToCalorConverter().Convert(source);
        Assert.True(converted.Success, string.Join("\n", converted.Context.Issues));
        Assert.DoesNotContain("raw ", converted.CalorSource);
        Assert.DoesNotContain('§', converted.CalorSource!);
        Assert.Equal(0, converted.InteropPreservationCount);
        Assert.Equal(0, converted.LossySubstitutionCount);
        Assert.Equal(0, converted.DropCount);
        foreach (var candidate in new[] { converted.CalorSource!,
            new CalorEmitter().Emit(Parse(converted.CalorSource!)) })
        {
            var diagnostics = new DiagnosticBag();
            var bound = new Calor.Compiler.Binding.Binder(diagnostics).Bind(Parse(candidate));
            Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
            var members = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundFieldAccessExpression>().ToArray();
            Assert.Equal(2, members.Length);
            Assert.All(members, member =>
            {
                Assert.Equal("Cents", member.FieldName);
                Assert.Equal("LONG", member.Type.DisplayString);
                var property = Assert.Single(member.ResolvedFields);
                Assert.True(property.IsProperty);
                Assert.Equal("Money", property.DeclaringTypeName);
                Assert.IsType<BoundVariableExpression>(member.Target);
            });
            var result = Program.Compile(candidate);
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.Equal(Execute(source, AddMoney), Execute(result.GeneratedCode, AddMoney));
        }
    }

    [Fact]
    public void InterfaceIndexerPreservesBracketParametersAndAccessors()
    {
        var module = Parse("""
            module Indexers
              interface IBag
                this[int:@step, string:key] → int
                  get
                  set
            """);
        var emitted = new CalorEmitter().Emit(module);
        var indexer = Parse(emitted).Interfaces.Single().Indexers.Single();
        Assert.Equal(new[] { "step", "key" }, indexer.Parameters.Select(parameter => parameter.Name));
        Assert.NotNull(indexer.Getter);
        Assert.NotNull(indexer.Setter);
        var result = Program.Compile(emitted);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Execute(result.GeneratedCode, _ => 0);
    }

    [Theory]
    [InlineData("this(int:i) → int")]
    [InlineData("this[int:i]")]
    [InlineData("this[] → int")]
    [InlineData("this[int:i] → int")]
    [InlineData("op + (C:a, C:b)")]
    public void MalformedRetainedHeadersAreDiagnosed(string header)
    {
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer($"module Bad\n  class C\n    {header}", diagnostics)
            .TokenizeAllForParser(), diagnostics).Parse();
        Assert.True(diagnostics.HasErrors);
    }

    private static long ReadAndWriteIndexer(Assembly assembly)
    {
        var type = assembly.GetType("Bag", throwOnError: true)!;
        var instance = Activator.CreateInstance(type);
        var indexer = type.GetProperty("Item", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;
        indexer.SetValue(instance, 42, [1]);
        return (int)indexer.GetValue(instance, [1])!;
    }

    private static long AddMoney(Assembly assembly)
    {
        var type = assembly.GetType("Money", throwOnError: true)!;
        var a = Activator.CreateInstance(type, 17L);
        var b = type.GetMethod("op_Implicit")!.Invoke(null, [25L]);
        var sum = type.GetMethod("op_Addition")!.Invoke(null, [a, b]);
        return (long)type.GetProperty("Cents")!.GetValue(sum)!;
    }

    private static long Execute(string source, Func<Assembly, long> observe)
    {
        var compilation = CSharpCompilation.Create("RetainedCapabilities",
            [CSharpSyntaxTree.ParseText(source)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext("RetainedCapabilities", isCollectible: true);
        try { return observe(context.LoadFromStream(stream)); }
        finally { context.Unload(); }
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return module;
    }
}
