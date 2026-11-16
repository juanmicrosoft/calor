using System.Reflection;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public class PostconditionReturnLoweringRuntimeTests
{
    [Fact]
    public void NestedIfLoopTryFinallyUsing_PreservesTraceAndChecksOnce()
    {
        const string source = """
            module ReturnFlow ReturnFlow block
              func Record private (List<int>:trace, int:value) → int
                trace.Add(value)
                return value

              func Choose (List<int>:trace, bool:early) → int
                !
                => (Record(trace, result) == result)
                try
                  if early
                    for i from 0 to 0 step 1
                      using MemoryStream:stream = new MemoryStream()
                        trace.Add(10)
                        return Record(trace, 7)
                        trace.Add(99)
                finally
                  trace.Add(20)
                trace.Add(30)
                return 3
            """;
        var assembly = Compile(source, out _);
        var trace = new List<int>();

        var value = InvokeStatic(assembly, "ReturnFlowModule", "Choose", trace, true);

        Assert.Equal(7, value);
        Assert.Equal([10, 7, 20, 7], trace);
    }

    [Fact]
    public void ExceptionalExit_DoesNotRunPostcondition()
    {
        const string source = """
            module Exceptional Exceptional block
              func Record private (List<int>:trace, int:value) → int
                trace.Add(value)
                return value

              func Boom (List<int>:trace) → int
                !
                => (Record(trace, result) == result)
                try
                  return 5
                finally
                  throw "boom"
            """;
        var assembly = Compile(source, out _);
        var trace = new List<int>();

        var exception = Assert.Throws<TargetInvocationException>(
            () => InvokeStatic(assembly, "ExceptionalModule", "Boom", trace));

        Assert.IsType<Exception>(exception.InnerException);
        Assert.Empty(trace);
    }

    [Fact]
    public void ResultPseudoVariable_DoesNotAlterIdentifiersOrStrings()
    {
        const string source = """
            module ResultSafety ResultSafety block
              func Echo (int:resultCode, string:myresult) → int
                !
                => ((result == resultCode) && (myresult == "result")) && ("result stays literal".Length > 0)
                return resultCode
            """;
        var assembly = Compile(source, out var generatedCode);

        var value = InvokeStatic(
            assembly,
            "ResultSafetyModule",
            "Echo",
            42,
            "result");

        Assert.Equal(42, value);
        Assert.Contains("resultCode", generatedCode);
        Assert.Contains("myresult", generatedCode);
        Assert.Contains("result stays literal", generatedCode);
        Assert.DoesNotContain("__calorPostconditionResultCode", generatedCode);
    }

    [Fact]
    public async Task AsyncTaskAndTaskOfT_UseValueResultsAndCheckOnce()
    {
        const string source = """
            module AsyncReturns AsyncReturns block
              func Record private (List<int>:trace, int:value) → int
                trace.Add(value)
                return value

              func Get async (List<int>:trace, bool:first) → int
                !
                => (Record(trace, result) == result)
                if first
                  return 5
                return 6

              func Done async (List<int>:trace) → void
                !
                => (Record(trace, 1) == 1)
                return
            """;
        var assembly = Compile(source, out var generatedCode);
        var valueTrace = new List<int>();
        var voidTrace = new List<int>();

        var valueTask = Assert.IsAssignableFrom<Task>(
            InvokeStatic(
                assembly,
                "AsyncReturnsModule",
                "Get",
                valueTrace,
                true));
        await valueTask;
        var value = valueTask.GetType().GetProperty("Result")!.GetValue(valueTask);

        var voidTask = Assert.IsAssignableFrom<Task>(
            InvokeStatic(
                assembly,
                "AsyncReturnsModule",
                "Done",
                voidTrace));
        await voidTask;

        Assert.Equal(5, value);
        Assert.Equal([5], valueTrace);
        Assert.Equal([1], voidTrace);
        Assert.Contains("int __calorPostconditionResult", generatedCode);
        Assert.DoesNotContain(
            "Task<int> __calorPostconditionResult",
            generatedCode);
    }

    [Fact]
    public void IteratorPostcondition_IsRejectedExplicitly()
    {
        const string source = """
            module IteratorContracts IteratorContracts block
              func Values () → int
                !
                => (result >= 0)
                yield 1
            """;

        var result = Program.Compile(
            source,
            "iterator.calr",
            new CompilationOptions { EnforceEffects = false });

        Assert.True(result.HasErrors);
        Assert.Contains(
            result.Diagnostics,
            diagnostic =>
                diagnostic.Code == DiagnosticCode.IteratorPostconditionUnsupported);
    }

    [Fact]
    public void ClassMethodAndOperatorMethod_ShareReturnLowering()
    {
        const string source = """
            module MethodOwners MethodOwners block
              class Legacy
                func Pick (bool:first) → int
                  !
                  => (result >= 0)
                  if first
                    return 4
                  return 5

                func op_Implicit static (Legacy:value) → int
                  !
                  => (result >= 0)
                  if (value != null)
                    return 8
                  return 9
            """;
        var assembly = Compile(source, out _);
        var type = GetType(assembly, "Legacy");
        var instance = Activator.CreateInstance(type)!;

        var methodValue = type.GetMethod("Pick")!.Invoke(instance, [true]);
        var operatorValue = type.GetMethod("op_Implicit")!.Invoke(null, [instance]);

        Assert.Equal(4, methodValue);
        Assert.Equal(8, operatorValue);
    }

    [Fact]
    public void OperatorOverload_UsesSharedReturnLowering()
    {
        const string source = """
            module OperatorOwner OperatorOwner block
              class Box
                op + (Box:left, Box:right) → Box
                  !
                  => (result != null)
                  if (left != null)
                    return left
                  return right
            """;
        var assembly = Compile(source, out _);
        var type = GetType(assembly, "Box");
        var left = Activator.CreateInstance(type)!;
        var right = Activator.CreateInstance(type)!;

        var value = type.GetMethod("op_Addition")!.Invoke(null, [left, right]);

        Assert.Same(left, value);
    }

    [Fact]
    public void EnumExtension_UsesSharedReturnLowering()
    {
        const string source = """
            module EnumOwner EnumOwner block
              enum Color
                Red
                Blue

              module ColorExtensions
                func Code (this Color:self, bool:primary) → int
                  !
                  => (result >= 0)
                  if primary
                    return 1
                  return 2
            """;
        var assembly = Compile(source, out _);
        var enumType = GetType(assembly, "Color");
        var extensionType = GetType(assembly, "ColorExtensions");
        var red = Enum.Parse(enumType, "Red");

        var value = extensionType.GetMethod("Code")!.Invoke(
            null,
            [red, true]);

        Assert.Equal(1, value);
    }

    [Fact]
    public void StatementLambda_ReturnDoesNotInheritOuterLowering()
    {
        const string source = """
            module LambdaOwner LambdaOwner block
              func Make () → Func<int> !
                !
                => (result == result)
                return () =>
                  return 42
            """;
        var assembly = Compile(source, out var generatedCode);

        var value = Assert.IsAssignableFrom<Delegate>(
            InvokeStatic(assembly, "LambdaOwnerModule", "Make"));

        Assert.Equal(42, value.DynamicInvoke());
        Assert.Contains("return 42;", generatedCode);
    }

    [Fact]
    public void NonVoidFallthrough_RemainsACompilerErrorWithoutDefaultResult()
    {
        const string source = """
            module MissingReturn MissingReturn block
              func Maybe (bool:take) → int
                !
                => (result >= 0)
                if take
                  return 1
            """;

        var (generatedCode, diagnostics) = Emit(
            source,
            ContractMode.Debug);
        var csharpDiagnostics = GetCSharpDiagnostics(generatedCode);

        Assert.False(
            diagnostics.HasErrors,
            string.Join(Environment.NewLine, diagnostics.Errors));
        Assert.Contains("int __calorPostconditionResult0;", generatedCode);
        Assert.DoesNotContain(
            "__calorPostconditionResult0 = default",
            generatedCode);
        Assert.Contains(
            csharpDiagnostics,
            diagnostic => diagnostic.Id == "CS0165");
    }

    public static TheoryData<string, string> NestedYieldContainers => new()
    {
        {
            "using",
            """
            using MemoryStream:stream = new MemoryStream()
              yield 1
            """
        },
        {
            "preprocessor-body",
            """
            pp DEBUG
              yield 1
            """
        },
        {
            "preprocessor-else",
            """
            pp DEBUG
              Console.WriteLine(0)
            pp else
              yield 1
            """
        },
        {
            "match",
            """
            match value
              case _
                yield 1
            """
        },
        {
            "dictionary-foreach",
            """
            each key, item in items
              yield item
            """
        },
        {
            "try",
            """
            try
              yield 1
            finally
              Console.WriteLine(0)
            """
        },
        {
            "catch",
            """
            try
              Console.WriteLine(0)
            catch Exception:ex
              yield 1
            """
        },
        {
            "finally",
            """
            try
              Console.WriteLine(0)
            finally
              yield 1
            """
        },
        {
            "unsafe",
            """
            unsafe
              yield 1
            """
        },
        {
            "fixed",
            """
            fixed int*:pointer = array
              yield 1
            """
        },
        {
            "sync",
            """
            lock gate
              yield 1
            """
        },
    };

    [Theory]
    [MemberData(nameof(NestedYieldContainers))]
    public void NestedYield_InStatementContainer_IsDetected(
        string container,
        string nestedBody)
    {
        var source = $$"""
            module NestedYield NestedYield block
              func Values (int:value, Dictionary<string, int>:items, int[]:array, object:gate) → int
                !
                => (result >= 0)
            {{Indent(nestedBody, 4)}}
            """;

        var (_, diagnostics) = Emit(source, ContractMode.Debug);

        Assert.Contains(
            diagnostics,
            diagnostic =>
                diagnostic.Code == DiagnosticCode.IteratorPostconditionUnsupported
                && diagnostic.Message.Contains(
                    "'Values'",
                    StringComparison.Ordinal));
        Assert.False(string.IsNullOrWhiteSpace(container));
    }

    [Fact]
    public void ResultPseudoVariable_RespectsLexicalBinders()
    {
        const string source = """
            module QuantifiedResult QuantifiedResult block
              func Zero () → int
                !
                => (forall int:result in 0..1 : (result == 0))
                => (exists int:result in 0..1 : (result == 0))
                => (!((result is int result)) || (result >= 0))
                => (result == 0)
                return 0
            """;

        var assembly = Compile(source, out var generatedCode);
        var value = InvokeStatic(
            assembly,
            "QuantifiedResultModule",
            "Zero");

        Assert.Equal(0, value);
        Assert.Contains(
            ".All(result => ((!(result >= 0",
            generatedCode);
        Assert.Contains(
            ".Any(result => (result >= 0 && result < 1 && result == 0))",
            generatedCode);
        Assert.Contains(
            "__calorPostconditionResult0 is int result) || (result >= 0)",
            generatedCode);
        Assert.Contains(
            "if (!(__calorPostconditionResult",
            generatedCode);
    }

    [Fact]
    public void PatternBinder_DoesNotShadowResultOnFalseBranch()
    {
        const string source = """
            module PatternResult PatternResult block
              func Maybe (object:fallback) → object
                !
                => ((result is int result) || (result == fallback))
                return fallback
            """;

        var assembly = Compile(source, out var generatedCode);
        var value = InvokeStatic(
            assembly,
            "PatternResultModule",
            "Maybe",
            new object[] { null! });

        Assert.Null(value);
        Assert.Contains(
            "__calorPostconditionResult0 is int result || __calorPostconditionResult0 == fallback",
            generatedCode);
    }

    [Fact]
    public void Disjunction_BindsResultOnlyWhenEveryTruePathDoes()
    {
        const string source = """
            module PatternTruthFlow PatternTruthFlow block
              func Value () → object
                !
                => (((result is int result) || false) && (result > 0))
                return 1
            """;

        var assembly = Compile(source, out var generatedCode);
        var value = InvokeStatic(
            assembly,
            "PatternTruthFlowModule",
            "Value");

        Assert.Equal(1, value);
        Assert.Contains(
            "(__calorPostconditionResult0 is int result || false) && result > 0",
            generatedCode);
    }

    [Fact]
    public void NegatedPattern_BindsResultOnFalseContinuation()
    {
        const string source = """
            module NegatedPattern NegatedPattern block
              func Value () → object
                !
                => ((!(result is int result)) || (result > 0))
                return 1
            """;

        var assembly = Compile(source, out var generatedCode);
        var value = InvokeStatic(
            assembly,
            "NegatedPatternModule",
            "Value");

        Assert.Equal(1, value);
        Assert.Contains(
            "!(__calorPostconditionResult0 is int result) || result > 0",
            generatedCode);
    }

    [Fact]
    public void SynthesizedReturnNames_AvoidParametersLocalsAndLabels()
    {
        const string source = """
            module Hygiene Hygiene block
              func ParameterCollision (int:__calorPostconditionResult0) → int
                !
                => (result >= 0)
                __calorPostconditionExit0:
                return __calorPostconditionResult0

              func LocalCollision () → int
                !
                => (result >= 0)
                let int:__calorPostconditionResult1 = 2
                __calorPostconditionExit1:
                return __calorPostconditionResult1
            """;

        var assembly = Compile(source, out var generatedCode);
        var parameterValue = InvokeStatic(
            assembly,
            "HygieneModule",
            "ParameterCollision",
            1);
        var localValue = InvokeStatic(
            assembly,
            "HygieneModule",
            "LocalCollision");

        Assert.Equal(1, parameterValue);
        Assert.Equal(2, localValue);
        Assert.Contains("__calorPostconditionResult0_1", generatedCode);
        Assert.Contains("__calorPostconditionExit0_1", generatedCode);
        Assert.Contains("__calorPostconditionResult1_1", generatedCode);
        Assert.Contains("__calorPostconditionExit1_1", generatedCode);
    }

    [Fact]
    public void SynthesizedReturnNames_AvoidPostconditionBinders()
    {
        const string source = """
            module PostconditionHygiene PostconditionHygiene block
              func Value () → object
                !
                => (!((result is int __calorPostconditionResult0)) || (result is object))
                return 1
            """;

        var assembly = Compile(source, out var generatedCode);
        var value = InvokeStatic(
            assembly,
            "PostconditionHygieneModule",
            "Value");

        Assert.Equal(1, value);
        Assert.Contains(
            "object __calorPostconditionResult0_1;",
            generatedCode);
        Assert.Contains(
            "is int __calorPostconditionResult0",
            generatedCode);
    }

    [Fact]
    public void SynthesizedReturnNames_AvoidMethodTypeParameters()
    {
        const string source = """
            module GenericHygiene GenericHygiene block
              func Value<__calorPostconditionResult0> () → int
                !
                => (result >= 0)
                return 1
            """;

        Compile(source, out var generatedCode);

        Assert.Contains(
            "int __calorPostconditionResult0_1;",
            generatedCode);
    }

    private static (string Code, DiagnosticBag Diagnostics) Emit(
        string source,
        ContractMode contractMode)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("return-lowering.calr");
        var lexer = new Lexer(source, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
        var module = parser.Parse();
        Assert.False(
            diagnostics.HasErrors,
            string.Join(Environment.NewLine, diagnostics.Errors));

        var emitter = new CSharpEmitter(
            contractMode,
            null,
            null,
            null,
            diagnostics);
        return (emitter.Emit(module), diagnostics);
    }

    private static IReadOnlyList<Microsoft.CodeAnalysis.Diagnostic> GetCSharpDiagnostics(
        string generatedCode)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            GeneratedCSharpCompiler.GlobalUsingsPreamble + generatedCode);
        return CSharpCompilation.Create(
                $"PostconditionReturnLoweringDiagnostics_{Guid.NewGuid():N}",
                [syntaxTree],
                GeneratedCSharpCompiler.References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .GetDiagnostics();
    }

    private static string Indent(string value, int spaces)
    {
        var prefix = new string(' ', spaces);
        return string.Join(
            Environment.NewLine,
            value.Trim().Split('\n')
                .Select(line => prefix + line.TrimEnd('\r')));
    }

    private static Assembly Compile(string source, out string generatedCode)
    {
        var result = Program.Compile(
            source,
            "return-lowering.calr",
            new CompilationOptions
            {
                ContractMode = ContractMode.Debug,
                EnforceEffects = false,
                EnableTypeChecking = false,
            });
        Assert.False(
            result.HasErrors,
            string.Join(Environment.NewLine, result.Diagnostics.Errors));
        generatedCode = result.GeneratedCode;

        var syntaxTree = CSharpSyntaxTree.ParseText(
            GeneratedCSharpCompiler.GlobalUsingsPreamble + generatedCode);
        var compilation = CSharpCompilation.Create(
            $"PostconditionReturnLowering_{Guid.NewGuid():N}",
            [syntaxTree],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(
            emit.Success,
            string.Join(Environment.NewLine, emit.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    private static object? InvokeStatic(
        Assembly assembly,
        string typeName,
        string methodName,
        params object?[] arguments)
        => GetType(assembly, typeName)
            .GetMethod(methodName)!
            .Invoke(null, arguments);

    private static Type GetType(Assembly assembly, string typeName)
        => Assert.Single(
            assembly.GetTypes(),
            type => type.Name == typeName);
}
