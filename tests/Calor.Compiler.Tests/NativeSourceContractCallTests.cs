using System.Reflection;
using System.Text.Json;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class NativeSourceContractCallTests
{
    private const string Original = """
        module FunctionCallBoundary
          func Positive (int:value) → bool
            !
            return value > 0
          func Identity (int:x | (# > 0)) → int
            !
            => Positive(result)
            return x
        """ + "\n";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SelectedSourcePredicate_ExecutesPreAndPostconditions(bool qualified, bool precondition)
    {
        var source = qualified ? """
            module FunctionCallBoundary
              module Predicates
                func Positive (int:value) → bool
                  !
                  return value > 0
              func Identity (int:x) → int
                !
                CONTRACT
                return x
            """ + "\n" : Original.Replace("    requires x > 0\n", "", StringComparison.Ordinal)
                .Replace("    => Positive(result)", "    CONTRACT", StringComparison.Ordinal);
        source = source.Replace("CONTRACT",
            $"{(precondition ? "requires" : "=>")} {(qualified ? "Predicates." : "")}Positive({(precondition ? "x" : "result")})",
            StringComparison.Ordinal);
        var result = Program.Compile(source, "source-contract.calr");
        Assert.False(result.HasErrors, Errors(result));
        var method = Load(result.GeneratedCode).GetType("GlobalModule")!.GetMethod("Identity")!;
        Assert.Equal(1, method.Invoke(null, [1]));
        var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [-1]));
        Assert.Equal("ContractViolationException", exception.InnerException!.GetType().Name);
    }

    [Fact]
    public void RealCli_OriginalDefaultRootAndGeneratedCodeExecute()
    {
        var root = Path.Combine(Path.GetTempPath(), "calor-source-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "boundary.calr"), Original);
            var result = CliTestHarness.RunCli(root, "--input", "boundary.calr", "--format", "json");
            Assert.True(result.ExitCode == 0, result.StdOut + result.StdErr);
            using var json = JsonDocument.Parse(result.StdOut);
            Assert.Empty(json.RootElement.GetProperty("diagnostics").EnumerateArray());
            var generated = File.ReadAllText(Path.Combine(root, "boundary.g.cs"));
            Assert.Contains("Positive(", generated);
            Assert.Equal(1, Load(generated).GetType("GlobalModule")!.GetMethod("Identity")!.Invoke(null, [1]));
            Assert.Equal(Original, File.ReadAllText(Path.Combine(root, "boundary.calr")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("requires Positive(x)")]
    [InlineData("=> Positive(result)")]
    public void SelectedEffectfulPredicate_CannotEscapePureCaller(string contract)
    {
        var source = Original.Replace("    requires x > 0\n    => Positive(result)", "    " + contract,
                StringComparison.Ordinal)
            .Replace("func Positive (int:value) → bool\n    !",
                "func Positive (int:value) → bool\n    ! cw\n    Console.WriteLine(value)", StringComparison.Ordinal);
        var result = Program.Compile(source, "effect-contract.calr");
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
            && error.Message.Contains("Identity", StringComparison.Ordinal)
            && error.Message.Contains("cw", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Missing(x)")]
    [InlineData("Positive(\"text\")")]
    [InlineData("Number(x)")]
    public void UnselectedOrNonBooleanCalls_AreRejected(string predicate)
    {
        var source = Original.Replace("    requires x > 0\n    => Positive(result)",
            "    requires " + predicate, StringComparison.Ordinal) + """
              func Number (int:value) → int
                !
                return value
            """ + "\n";
        var result = Program.Compile(source, "invalid-source-contract.calr");
        Assert.True(result.HasErrors, result.GeneratedCode);
        Assert.Contains(result.Diagnostics.Errors, error => error.Code == DiagnosticCode.TypeMismatch
            || error.Code == DiagnosticCode.NoMatchingOverload || error.Code == DiagnosticCode.UndefinedReference
            || error.Code == DiagnosticCode.ForbiddenEffect);
    }

    private static string Errors(CompilationResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Errors);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuntimeRepair_DoesNotProveSourceFunctionCalls(bool precondition)
    {
        var source = Original.Replace("    requires x > 0\n    => Positive(result)",
            precondition ? "    requires Positive(x)" : "    => Positive(result)", StringComparison.Ordinal);
        var options = new CompilationOptions
        {
            VerifyContracts = true,
            ElideProvenGuards = true,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false }
        };
        var result = Program.Compile(source, "unmodeled-source-call.calr", options);
        Assert.False(result.HasErrors, Errors(result));
        var verification = Assert.Single(options.VerificationResults!.Functions.Where(function => function.FunctionName == "Identity"));
        var predicate = Assert.Single(precondition ? verification.PreconditionResults : verification.PostconditionResults);
        Assert.Equal(ContractVerificationStatus.Unsupported, predicate.Status);
        Assert.DoesNotContain("// PROVEN", result.GeneratedCode);
        var method = Load(result.GeneratedCode).GetType("GlobalModule")!.GetMethod("Identity")!;
        Assert.Equal(1, method.Invoke(null, [1]));
        Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [-1]));
    }

    [Theory]
    [InlineData(ContractMode.Debug)]
    [InlineData(ContractMode.Off)]
    public void SelectedReceiverAndArguments_EvaluateOnceInOrder(ContractMode mode)
    {
        var result = Program.Compile("""
            module ContractOrder
              class Predicate
                func Positive (int:value) → bool
                  ! cw
                  Console.WriteLine("predicate")
                  return value > 0
              func Receiver () → Predicate
                ! cw,alloc
                Console.WriteLine("receiver")
                return new Predicate()
              func Argument (string:label, int:value) → int
                ! cw
                Console.WriteLine(label)
                return value
              func Identity (int:x | (Receiver().Positive(Argument("entry", #)))) → int
                ! cw,alloc
                => Receiver().Positive(Argument("exit", result))
                Console.WriteLine("body")
                return x
            """, "ordered-contract.calr", new CompilationOptions { ContractMode = mode });
        Assert.False(result.HasErrors, Errors(result));
        var method = Load(result.GeneratedCode).GetType("GlobalModule")!.GetMethod("Identity")!;
        var previous = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(1, method.Invoke(null, [1]));
            Assert.Equal(mode == ContractMode.Off ? ["body"]
                : new[] { "receiver", "entry", "predicate", "body", "receiver", "exit", "predicate" },
                output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
            if (mode == ContractMode.Off)
                Assert.Equal(-1, method.Invoke(null, [-1]));
        }
        finally
        {
            Console.SetOut(previous);
        }
    }

    [Fact]
    public void FalsePostcondition_IsRejectedButContractOffPreservesBody()
    {
        var source = Original.Replace("    return x", "    return -1", StringComparison.Ordinal);
        foreach (var mode in new[] { ContractMode.Debug, ContractMode.Off })
        {
            var result = Program.Compile(source, "false-postcondition.calr", new CompilationOptions { ContractMode = mode });
            Assert.False(result.HasErrors, Errors(result));
            var method = Load(result.GeneratedCode).GetType("GlobalModule")!.GetMethod("Identity")!;
            if (mode == ContractMode.Off)
                Assert.Equal(-1, method.Invoke(null, [1]));
            else
                Assert.Equal("ContractViolationException",
                    Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [1])).InnerException!.GetType().Name);
        }
    }

    [Theory]
    [InlineData("requires Predicates.Positive(x)", "int:x", "private", "int:value")]
    [InlineData("requires Predicates.Positive(x)", "int:x, int:Predicates", "public", "int:value")]
    [InlineData("requires Predicates.Positive(missing)", "int:x", "public", "int:value")]
    [InlineData("requires Predicates.Positive(x)", "int:x", "public", "string:value")]
    [InlineData("requires Other.Positive(x)", "int:x", "public", "int:value")]
    public void InvalidCompetitors_DoNotBorrowThePurePredicate(
        string contract, string parameters, string visibility, string predicateParameters)
    {
        var source = $$"""
            module NegativeSourceContracts
              module Predicates
                func Positive {{visibility}} ({{predicateParameters}}) → bool
                  {{(visibility == "private" ? "" : "!\n      ")}}return true
              func Identity ({{parameters}}) → int
                !
                {{contract}}
                return x
            """;
        var result = Program.Compile(source, "source-contract-competitor.calr");
        Assert.True(result.HasErrors, result.GeneratedCode);
        Assert.DoesNotContain(result.Diagnostics.Errors, error => error.Code == DiagnosticCode.AnalysisICE);
        Assert.DoesNotContain(result.Diagnostics.Errors, error => error.Code.StartsWith("Calor01", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
            || error.Code == DiagnosticCode.UndefinedReference || error.Code == DiagnosticCode.NoMatchingOverload
            || error.Code == DiagnosticCode.InvalidReference || error.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void AmbiguousPredicates_DoNotSelectByName()
    {
        var result = Program.Compile("""
            module AmbiguousContract
              class Left
              class Right
              func Positive (Left:value) → bool
                !
                return true
              func Positive (Right:value) → bool
                !
                return true
              func Identity (int:x) → int
                !
                requires Positive(null)
                return x
            """, "ambiguous-contract.calr");
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, error => error.Code == DiagnosticCode.AmbiguousOverload
            || error.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void OpaqueOverload_CannotBorrowSelectedSourcePurity()
    {
        const string opaque = "public static bool Positive(params object[] values) { System.Console.WriteLine(\"opaque\"); return true; }";
        var source = $$"""
            module OpaqueContract
              module Predicates
                raw member {{System.Text.Encoding.UTF8.GetByteCount(opaque)}}
            {{opaque}}
                func Positive<T> (T:value) → bool
                  !
                  return true
              func Identity (object[]:values | (Predicates.Positive(#))) → int
                !
                return 1
            """;
        var result = Program.Compile(source, "opaque-contract.calr");
        Assert.True(result.HasErrors, result.GeneratedCode);
        Assert.Contains(result.Diagnostics.Errors, error => error.Code == DiagnosticCode.InvalidCSharpInteropReason);
        var diagnostics = new DiagnosticBag();
        var module = new Calor.Compiler.Parsing.Parser(
            new Calor.Compiler.Parsing.Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        var bound = new Calor.Compiler.Binding.Binder(diagnostics).Bind(module);
        var graph = Calor.Compiler.Analysis.CallGraphAnalysis.BuildResolved(bound);
        var caller = bound.Functions.Single(function => function.Symbol.Name == "Identity");
        Assert.Empty(graph.ForwardGraph[caller.SymbolId]);
        Assert.Contains(graph.UnresolvedCalls, call => call.Caller == caller.SymbolId);
        new Calor.Compiler.Effects.EffectEnforcementPass(diagnostics).Enforce(module);
        Assert.Contains(diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
            || error.Code == DiagnosticCode.UnknownExternalCall);
        Assert.DoesNotContain(result.Diagnostics.Errors, error => error.Code == DiagnosticCode.AnalysisICE);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PurePredicate_DoesNotHideReceiverOrArgumentEffects(bool receiverWrites, bool permitsWrite)
    {
        var source = $$"""
            module EvaluationEffects
              class Predicate
                func Positive (int:value) → bool
                  !
                  return value > 0
              func Receiver (Predicate:value) → Predicate
                {{(receiverWrites ? "! cw" : "!")}}
                {{(receiverWrites ? "Console.WriteLine(\"receiver\")" : "")}}
                return value
              func Argument (int:value) → int
                {{(receiverWrites ? "!" : "! cw")}}
                {{(receiverWrites ? "" : "Console.WriteLine(\"argument\")")}}
                return value
              func Identity (Predicate:predicate, int:x) → int
                {{(permitsWrite ? "! cw" : "!")}}
                requires Receiver(predicate).Positive(Argument(x))
                return x
            """;
        var result = Program.Compile(source, "contract-evaluation-effects.calr");
        if (permitsWrite)
        {
            Assert.False(result.HasErrors, Errors(result));
            var assembly = Load(result.GeneratedCode);
            Assert.Equal(1, assembly.GetType("GlobalModule")!.GetMethod("Identity")!.Invoke(null,
                [Activator.CreateInstance(assembly.GetType("Predicate")!), 1]));
        }
        else
            Assert.Contains(result.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
                && error.Message.Contains("Identity", StringComparison.Ordinal)
                && error.Message.Contains("cw", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("func Read (int:x) → int\n      !\n      requires Predicates.Positive(x)\n      => Predicates.Positive(result)\n      return x", "method")]
    [InlineData("ctor (int:x)\n      requires Predicates.Positive(x)", "constructor")]
    [InlineData("property int:Value\n      get\n        requires Predicates.Positive(1)\n        return 1", "accessor")]
    [InlineData("this [int:x] → int\n      get\n        requires Predicates.Positive(x)\n        return x", "indexer")]
    [InlineData("op + (Box:left, Box:right) → Box\n      !\n      requires Predicates.Positive(1)\n      => Predicates.Positive(1)\n      return left", "operator")]
    public void ExistingMemberContractSurfaces_BindSourcePredicates(string member, string kind)
    {
        var result = Program.Compile($$"""
            module MemberContracts
              module Predicates
                func Positive (int:value) → bool
                  !
                  return value > 0
              class Box
                {{member}}
            """, $"source-contract-{kind}.calr");
        Assert.False(result.HasErrors, Errors(result));
        var summary = Calor.Compiler.Effects.EffectSummaryBuilder.Build(result.Ast!);
        Assert.Contains(summary.Callers, caller => caller.Calls.Any(call =>
            call.Target.EndsWith(".Positive", StringComparison.Ordinal)));
        var type = Load(result.GeneratedCode).GetType("Box")!;
        if (kind == "constructor")
        {
            Assert.NotNull(Activator.CreateInstance(type, [1]));
            Assert.Equal("ContractViolationException",
                Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(type, [-1])).InnerException!.GetType().Name);
        }
        else
        {
            var instance = Activator.CreateInstance(type);
            if (kind == "method" || kind == "indexer")
            {
                var method = type.GetMethod(kind == "method" ? "Read" : "get_Item")!;
                Assert.Equal(1, method.Invoke(instance, [1]));
                Assert.Equal("ContractViolationException",
                    Assert.Throws<TargetInvocationException>(() => method.Invoke(instance, [-1])).InnerException!.GetType().Name);
            }
            else if (kind == "accessor")
                Assert.Equal(1, type.GetProperty("Value")!.GetValue(instance));
            else
                Assert.Same(instance, type.GetMethod("op_Addition")!.Invoke(null, [instance, instance]));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Overloads_UseTheActualSelectedRow(bool effectful)
    {
        var result = Program.Compile($$"""
            module OverloadContract
              func Positive (int:value) → bool
                !
                return value > 0
              func Positive (string:value) → bool
                ! cw
                Console.WriteLine(value)
                return true
              func Identity (int:x) → int
                !
                requires Positive({{(effectful ? "\"effectful\"" : "x")}})
                return x
            """, "overloaded-contract.calr");
        if (effectful)
            Assert.Contains(result.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
                && error.Message.Contains("cw", StringComparison.Ordinal));
        else
        {
            Assert.False(result.HasErrors, Errors(result));
            Assert.Equal(1, Load(result.GeneratedCode).GetType("GlobalModule")!.GetMethod("Identity")!.Invoke(null, [1]));
        }
    }

    [Theory]
    [InlineData("func Read (int:x) → int\n      !\n      requires Predicates.Positive(x)\n      return x", "Calor0410")]
    [InlineData("ctor (int:x)\n      requires Predicates.Positive(x)", "Calor0422")]
    [InlineData("property int:Value\n      get\n        requires Predicates.Positive(1)\n        return 1", "Calor0423")]
    [InlineData("this [int:x] → int\n      get\n        requires Predicates.Positive(x)\n        return x", "Calor0423")]
    [InlineData("op + (Box:left, Box:right) → Box\n      !\n      requires Predicates.Positive(1)\n      return left", "Calor0410")]
    public void MemberContracts_CannotHideSelectedConsoleEffects(string member, string expectedCode)
    {
        var result = Program.Compile($$"""
            module EffectfulMemberContracts
              module Predicates
                func Positive (int:value) → bool
                  ! cw
                  Console.WriteLine(value)
                  return value > 0
              class Box
                {{member}}
            """, "effectful-member-contract.calr");
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, error => error.Code == expectedCode
            && error.Message.Contains("cw", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("func Read (int:x) → int\n      !\n      requires Predicates.Number(x)\n      return x")]
    [InlineData("ctor (int:x)\n      requires Predicates.Number(x)")]
    [InlineData("property int:Value\n      get\n        requires Predicates.Number(1)\n        return 1")]
    [InlineData("this [int:x] → int\n      get\n        requires Predicates.Number(x)\n        return x")]
    [InlineData("op + (Box:left, Box:right) → Box\n      !\n      requires Predicates.Number(1)\n      return left")]
    public void MemberContracts_RejectSelectedNonBooleanReturns(string member)
    {
        var result = Program.Compile($$"""
            module InvalidMemberContracts
              module Predicates
                func Number (int:value) → int
                  !
                  return value
              class Box
                {{member}}
            """, "nonboolean-member-contract.calr");
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, error => error.Code == DiagnosticCode.TypeMismatch);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterfaceDeclarations_CheckTheirOwnSelectedContractEffects(bool writes)
    {
        var result = Program.Compile($$"""
            module InterfaceContract
              module Predicates
                func Positive (int:value) → bool
                  {{(writes ? "! cw" : "!")}}
                  {{(writes ? "Console.WriteLine(value)" : "")}}
                  return value > 0
              interface IGuard
                func Read (int:x | (Predicates.Positive(#))) → int
                  !
            """, "interface-source-contract.calr");
        if (writes)
            Assert.Contains(result.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
                && error.Message.Contains("cw", StringComparison.Ordinal));
        else
        {
            Assert.False(result.HasErrors, Errors(result));
            Load(result.GeneratedCode);
        }
    }

    [Theory]
    [InlineData(false, false, false, ContractMode.Debug)]
    [InlineData(false, true, false, ContractMode.Debug)]
    [InlineData(true, false, false, ContractMode.Debug)]
    [InlineData(true, true, false, ContractMode.Debug)]
    [InlineData(false, false, true, ContractMode.Debug)]
    [InlineData(false, true, true, ContractMode.Debug)]
    [InlineData(true, false, true, ContractMode.Debug)]
    [InlineData(true, true, true, ContractMode.Debug)]
    [InlineData(false, false, true, ContractMode.Off)]
    [InlineData(false, true, true, ContractMode.Off)]
    [InlineData(true, false, true, ContractMode.Off)]
    [InlineData(true, true, true, ContractMode.Off)]
    public void InheritedContracts_ChargeTheActualImplementationAndExecuteOnce(
        bool baseClass, bool postcondition, bool declared, ContractMode mode)
    {
        var source = $$"""
            module InheritedSourceContract
              module Predicates
                func Positive (int:value) → bool
                  ! cw
                  Console.WriteLine(value)
                  return value > 0
              {{(baseClass ? "class Base public abstract" : "interface IGuard")}}
                func Read {{(baseClass ? "public abstract " : "")}}(int:x) → int
                  ! cw
                  {{(postcondition ? "=> Predicates.Positive(result)" : "requires Predicates.Positive(x)")}}
              class Impl public
                {{(baseClass ? "extends Base" : "implements IGuard")}}
                func Read public{{(baseClass ? " override" : "")}} (int:input) → int
                  {{(declared ? "! cw" : "!")}}
                  return input
            """;
        var result = Program.Compile(source, "inherited-source-contract.calr",
            new CompilationOptions { ContractMode = mode });
        if (!declared)
        {
            Assert.Contains(result.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
                && error.Message.Contains("Read", StringComparison.Ordinal)
                && error.Message.Contains("cw", StringComparison.Ordinal));
            return;
        }

        Assert.False(result.HasErrors, Errors(result));
        var summary = Calor.Compiler.Effects.EffectSummaryBuilder.Build(result.Ast!);
        Assert.Contains(summary.Callers, caller => caller.CallerId.StartsWith("Impl.", StringComparison.Ordinal)
            && caller.Calls.Any(call => call.Target.EndsWith(".Positive", StringComparison.Ordinal)));
        var type = Load(result.GeneratedCode).GetType("Impl")!;
        var instance = Activator.CreateInstance(type);
        var method = type.GetMethod("Read")!;
        var previous = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(1, method.Invoke(instance, [1]));
            Assert.Equal(mode == ContractMode.Off ? "" : "1" + Environment.NewLine, output.ToString());
            if (mode == ContractMode.Off)
                Assert.Equal(-1, method.Invoke(instance, [-1]));
            else
                Assert.Equal("ContractViolationException",
                    Assert.Throws<TargetInvocationException>(() => method.Invoke(instance, [-1])).InnerException!.GetType().Name);
        }
        finally
        {
            Console.SetOut(previous);
        }
    }

    [Fact]
    public void GenericTupleContract_ReadsTheDeclaredTupleElement()
    {
        var result = Program.Compile("""
            module TupleContract
              func Read<T> ((T,int):x | (#.Item1 != null)) → (T,int)
                where T : class
                !
                return x
            """, "tuple-contract.calr");
        Assert.False(result.HasErrors, Errors(result));
        var method = Load(result.GeneratedCode).GetType("GlobalModule")!.GetMethod("Read")!.MakeGenericMethod(typeof(string));
        Assert.Equal(("value", 1), method.Invoke(null, [("value", 1)]));
        Assert.Equal("ContractViolationException",
            Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [((string?)null, 1)])).InnerException!.GetType().Name);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NestedMethods_CheckSelectedContractRows(bool writes, bool postcondition)
    {
        var result = Program.Compile($$"""
            module NestedSourceContract
              module Predicates
                func Positive (int:value) → bool
                  {{(writes ? "! cw" : "!")}}
                  {{(writes ? "Console.WriteLine(value)" : "")}}
                  return value > 0
              class Outer public
                class Inner public
                  func Identity public (int:x) → int
                    !
                    {{(postcondition ? "=> Predicates.Positive(result)" : "requires Predicates.Positive(x)")}}
                    return x
            """, "nested-source-contract.calr");
        if (writes)
            Assert.Contains(result.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
                && error.Message.Contains("cw", StringComparison.Ordinal));
        else
        {
            Assert.False(result.HasErrors, Errors(result));
            var type = Load(result.GeneratedCode).GetType("Outer+Inner")!;
            var method = type.GetMethod("Identity")!;
            var instance = Activator.CreateInstance(type);
            Assert.Equal(1, method.Invoke(instance, [1]));
            Assert.Equal("ContractViolationException",
                Assert.Throws<TargetInvocationException>(() => method.Invoke(instance, [-1])).InnerException!.GetType().Name);
        }
    }

    private static Assembly Load(string code)
    {
        var compilation = CSharpCompilation.Create("SourceContracts_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + code)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }
}
