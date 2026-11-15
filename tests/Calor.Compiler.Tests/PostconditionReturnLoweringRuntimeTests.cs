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
            §M{ResultSafety}
              §F{Echo:pub} (i32:resultCode, str:myresult) -> i32
                §S{"result stays literal"} (&& (== result resultCode) (== myresult STR:"result"))
                §R resultCode
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
            §M{IteratorContracts}
              §F{Values:pub} () -> i32
                §S (>= result 0)
                §YIELD INT:1
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
            §M{MethodOwners}
              §CL{Legacy:pub}
                §MT{Pick:pub} (bool:first) -> i32
                  §S (>= result 0)
                  §IF first
                    §R INT:4
                  §R INT:5
                §MT{op_Implicit:pub:stat} (Legacy:value) -> i32
                  §S (>= result 0)
                  §IF (!= value null)
                    §R INT:8
                  §R INT:9
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
            §M{OperatorOwner}
              §CL{Box:pub}
                §OP{+:pub}
                  §I{Box:left}
                  §I{Box:right}
                  §O{Box}
                  §S (!= result null)
                  §IF (!= left null)
                    §R left
                  §R right
                §/OP
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
            §M{EnumOwner}
              §EN{Color}
              Red
              Blue
              §/EN
              §EEXT{Color}
                §F{Code:pub} (Color:self, bool:primary) -> i32
                  §S (>= result 0)
                  §IF primary
                    §R INT:1
                  §R INT:2
              §/EEXT
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
              func Make () → Func<int>
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

    [Fact]
    public void OpaqueCSharp_InEitherPreprocessorBranch_IsRejected()
    {
        const string source = """
            §M{OpaquePreprocessor}
              §F{InBody:pub} () -> i32
                §S (>= result 0)
                §PP{DEBUG}
                  §RAW
                  return 1;
                  §/RAW
                §/PP{DEBUG}
              §F{InElse:pub} () -> i32
                §S (>= result 0)
                §PP{DEBUG}
                  §R INT:1
                §PPE
                  §RAW
                  return 2;
                  §/RAW
                §/PP{DEBUG}
            """;

        var (_, diagnostics) = Emit(source, ContractMode.Debug);
        var loweringErrors = diagnostics
            .Where(diagnostic =>
                diagnostic.Code == DiagnosticCode.PostconditionCheckNotLowered)
            .ToArray();

        Assert.Equal(2, loweringErrors.Length);
        Assert.Contains(
            loweringErrors,
            diagnostic => diagnostic.Message.Contains(
                "'InBody'",
                StringComparison.Ordinal));
        Assert.Contains(
            loweringErrors,
            diagnostic => diagnostic.Message.Contains(
                "'InElse'",
                StringComparison.Ordinal));
    }

    public static TheoryData<string, string> NestedYieldContainers => new()
    {
        {
            "using",
            """
            §USE{stream:MemoryStream} §NEW{MemoryStream} §/NEW
              §YIELD INT:1
            """
        },
        {
            "preprocessor-body",
            """
            §PP{DEBUG}
              §YIELD INT:1
            §/PP{DEBUG}
            """
        },
        {
            "preprocessor-else",
            """
            §PP{DEBUG}
              §P INT:0
            §PPE
              §YIELD INT:1
            §/PP{DEBUG}
            """
        },
        {
            "match",
            """
            §W value
              §K _
                §YIELD INT:1
            """
        },
        {
            "dictionary-foreach",
            """
            §EACHKV{key:item} items
              §YIELD item
            """
        },
        {
            "try",
            """
            §TR
              §YIELD INT:1
            §/TR
            """
        },
        {
            "catch",
            """
            §TR
              §P INT:0
            §CA{Exception:ex}
              §YIELD INT:1
            §/TR
            """
        },
        {
            "finally",
            """
            §TR
              §P INT:0
            §FI
              §YIELD INT:1
            §/TR
            """
        },
        {
            "unsafe",
            """
            §UNSAFE
              §YIELD INT:1
            §/UNSAFE
            """
        },
        {
            "fixed",
            """
            §FIXED{pointer:i32*:array}
              §YIELD INT:1
            §/FIXED
            """
        },
        {
            "sync",
            """
            §SYNC (gate)
              §YIELD INT:1
            §/SYNC
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
            §M{QuantifiedResult}
              §F{Zero:pub} () -> i32
                §S (forall ((result i32)) (-> (&& (>= result INT:0) (< result INT:1)) (== result INT:0)))
                §S (exists ((result i32)) (&& (>= result INT:0) (< result INT:1) (== result INT:0)))
                §S (-> (is result i32 result) (>= result INT:0))
                §S (== result INT:0)
                §R INT:0
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
    public void ContractModeOff_SkipsPostconditionRefusalsButKeepsRefinements()
    {
        const string source = """
            §M{ContractsOff}
              §RTYPE{NonNegative:i32} (>= # INT:0)
              §F{Iterator:pub} () -> NonNegative
                §S (>= result 0)
                §PP{DEBUG}
                  §YIELD INT:1
                §/PP{DEBUG}
              §F{Opaque:pub} () -> i32
                §S (>= result 0)
                §PP{DEBUG}
                  §RAW
                  return 1;
                  §/RAW
                §/PP{DEBUG}
              §F{Refined:pub} () -> NonNegative
                §S (>= result 0)
                §R INT:1
            """;

        var (generatedCode, diagnostics) = Emit(
            source,
            ContractMode.Off);

        Assert.DoesNotContain(
            diagnostics,
            diagnostic =>
                diagnostic.Code == DiagnosticCode.IteratorPostconditionUnsupported
                || diagnostic.Code == DiagnosticCode.PostconditionCheckNotLowered);
        Assert.Contains("IEnumerable<int> Iterator", generatedCode);
        Assert.Contains(
            "Yielded value violates refinement type 'NonNegative'",
            generatedCode);
        Assert.Contains("return 1;", generatedCode);
        Assert.Contains(
            "Return value violates refinement type 'NonNegative'",
            generatedCode);
        Assert.DoesNotContain("ContractKind.Ensures", generatedCode);
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
            §M{PostconditionHygiene}
              §F{Value:pub} () -> object
                §S (-> (is result i32 __calorPostconditionResult0) (is result object))
                §R INT:1
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
