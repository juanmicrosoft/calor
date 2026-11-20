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

    // #1618: a precondition is a refinement on the parameter. A contract starting with "|" is
    // placed on Identity's x; any other contract replaces the postcondition line.
    private static string WithContract(string contract)
    {
        var source = Original.Replace("(int:x | (# > 0))", "(int:x)", StringComparison.Ordinal);
        return contract.StartsWith('|')
            ? source.Replace("(int:x)", "(int:x " + contract + ")", StringComparison.Ordinal)
                .Replace("    => Positive(result)\n", "", StringComparison.Ordinal)
            : source.Replace("    => Positive(result)", "    " + contract, StringComparison.Ordinal);
    }

    private static string PreconditionFailure(bool precondition) =>
        precondition ? "ArgumentOutOfRangeException" : "ContractViolationException";

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
              func Identity (int:xREFINEMENT) → int
                !
                CONTRACT
                return x
            """ + "\n" : Original.Replace("(int:x | (# > 0))", "(int:xREFINEMENT)", StringComparison.Ordinal)
                .Replace("    => Positive(result)", "    CONTRACT", StringComparison.Ordinal);
        var predicate = $"{(qualified ? "Predicates." : "")}Positive({(precondition ? "#" : "result")})";
        source = source
            .Replace("REFINEMENT", precondition ? $" | ({predicate})" : "", StringComparison.Ordinal)
            .Replace(precondition ? "    CONTRACT\n" : "CONTRACT", precondition ? "" : "=> " + predicate,
                StringComparison.Ordinal);
        var result = Program.Compile(source, "source-contract.calr");
        Assert.False(result.HasErrors, Errors(result));
        var method = Load(result.GeneratedCode).GetType("GlobalModule")!.GetMethod("Identity")!;
        Assert.Equal(1, method.Invoke(null, [1]));
        var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [-1]));
        Assert.Equal(PreconditionFailure(precondition), exception.InnerException!.GetType().Name);
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
    [InlineData("| (Positive(#))")]
    [InlineData("=> Positive(result)")]
    public void SelectedEffectfulPredicate_CannotEscapePureCaller(string contract)
    {
        var source = WithContract(contract)
            .Replace("func Positive (int:value) → bool\n    !",
                "func Positive (int:value) → bool\n    ! cw\n    Console.WriteLine(value)", StringComparison.Ordinal);
        var result = Program.Compile(source, "effect-contract.calr");
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
            && error.Message.Contains("Identity", StringComparison.Ordinal)
            && error.Message.Contains("cw", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Missing(#)")]
    [InlineData("Positive(\"text\")")]
    [InlineData("Number(#)")]
    public void UnselectedOrNonBooleanCalls_AreRejected(string predicate)
    {
        var source = WithContract("| (" + predicate + ")") + """
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
        var source = WithContract(precondition ? "| (Positive(#))" : "=> Positive(result)");
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
    // #1618: the precondition is the refinement inside the parameter list.
    [InlineData("int:x | (Predicates.Positive(#))", "private", "int:value")]
    [InlineData("int:x, int:Predicates | (Predicates.Positive(x))", "public", "int:value")]
    [InlineData("int:x | (Predicates.Positive(missing))", "public", "int:value")]
    [InlineData("int:x | (Predicates.Positive(#))", "public", "string:value")]
    [InlineData("int:x | (Other.Positive(#))", "public", "int:value")]
    public void InvalidCompetitors_DoNotBorrowThePurePredicate(
        string parameters, string visibility, string predicateParameters)
    {
        var source = $$"""
            module NegativeSourceContracts
              module Predicates
                func Positive {{visibility}} ({{predicateParameters}}) → bool
                  {{(visibility == "private" ? "" : "!\n      ")}}return true
              func Identity ({{parameters}}) → int
                !
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
              func Identity (int:x | (Positive(null))) → int
                !
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
              func Identity (Predicate:predicate, int:x | (Receiver(predicate).Positive(Argument(#)))) → int
                {{(permitsWrite ? "! cw" : "!")}}
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
    // #1618: accessor and indexer-getter `requires` rows were removed: an accessor has no
    // parameter to refine, and an indexer parameter refinement is not yet enforced.
    [InlineData("func Read (int:x | (Predicates.Positive(#))) → int\n      !\n      => Predicates.Positive(result)\n      return x", "method")]
    [InlineData("ctor (int:x | (Predicates.Positive(#)))", "constructor")]
    [InlineData("op + (Box:left | (Predicates.Positive(1)), Box:right) → Box\n      !\n      => Predicates.Positive(1)\n      return left", "operator")]
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
            Assert.Equal("ArgumentOutOfRangeException",
                Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(type, [-1])).InnerException!.GetType().Name);
        }
        else
        {
            var instance = Activator.CreateInstance(type);
            if (kind == "method")
            {
                var method = type.GetMethod("Read")!;
                Assert.Equal(1, method.Invoke(instance, [1]));
                Assert.Equal("ArgumentOutOfRangeException",
                    Assert.Throws<TargetInvocationException>(() => method.Invoke(instance, [-1])).InnerException!.GetType().Name);
            }
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
              func Identity (int:x | (Positive({{(effectful ? "\"effectful\"" : "#")}}))) → int
                !
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
    // #1618: accessor and indexer-getter rows removed (see ExistingMemberContractSurfaces_BindSourcePredicates).
    [InlineData("func Read (int:x | (Predicates.Positive(#))) → int\n      !\n      return x", "Calor0410")]
    [InlineData("ctor (int:x | (Predicates.Positive(#)))", "Calor0422")]
    [InlineData("op + (Box:left | (Predicates.Positive(1)), Box:right) → Box\n      !\n      return left", "Calor0410")]
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
    // #1618: accessor and indexer-getter rows removed (see ExistingMemberContractSurfaces_BindSourcePredicates).
    [InlineData("func Read (int:x | (Predicates.Number(#))) → int\n      !\n      return x")]
    [InlineData("ctor (int:x | (Predicates.Number(#)))")]
    [InlineData("op + (Box:left | (Predicates.Number(1)), Box:right) → Box\n      !\n      return left")]
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
                func Read {{(baseClass ? "public abstract " : "")}}(int:x{{(postcondition ? "" : " | (Predicates.Positive(#))")}}) → int
                  ! cw
                  {{(postcondition ? "=> Predicates.Positive(result)" : "")}}
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
                Assert.Equal(PreconditionFailure(!postcondition),
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
                  func Identity public (int:x{{(postcondition ? "" : " | (Predicates.Positive(#))")}}) → int
                    !
                    {{(postcondition ? "=> Predicates.Positive(result)" : "")}}
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
            Assert.Equal(PreconditionFailure(!postcondition),
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
