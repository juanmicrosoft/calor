using System.Reflection;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public class PostconditionReturnLoweringRuntimeTests : IDisposable
{
    // #1150: generated assemblies go into collectible contexts, unloaded when xUnit
    // disposes this instance — i.e. as soon as the test that made them finishes.
    private readonly CollectibleAssemblyLoader _assemblies = new();

    public void Dispose() => _assemblies.Dispose();

    [Fact]
    public void NestedIfLoopTryFinallyUsing_PreservesTraceAndChecksOnce()
    {
        const string source = """
            §M{m001:ReturnFlow}
              §F{f001:Record:priv} (List<i32>:trace, i32:value) -> i32
                §C{trace.Add} §A value §/C
                §R value
              §F{f002:Choose:pub} (List<i32>:trace, bool:early) -> i32
                §S (== §C{Record} §A trace §A result §/C result)
                §TR{t1}
                  §IF{i1} early
                    §L{l1:i:0:0:1}
                      §USE{u1:stream:MemoryStream} §NEW{MemoryStream} §/NEW
                        §C{trace.Add} §A INT:10 §/C
                        §R §C{Record} §A trace §A INT:7 §/C
                        §C{trace.Add} §A INT:99 §/C
                  §FI
                    §C{trace.Add} §A INT:20 §/C
                §/TR{t1}
                §C{trace.Add} §A INT:30 §/C
                §R INT:3
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
            §M{m001:Exceptional}
              §F{f001:Record:priv} (List<i32>:trace, i32:value) -> i32
                §C{trace.Add} §A value §/C
                §R value
              §F{f002:Boom:pub} (List<i32>:trace) -> i32
                §S (== §C{Record} §A trace §A result §/C result)
                §TR{t1}
                  §R INT:5
                §FI
                  §TH STR:"boom"
                §/TR{t1}
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
            §M{m001:ResultSafety}
              §F{f001:Echo:pub} (i32:resultCode, str:myresult) -> i32
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
            §M{m001:AsyncReturns}
              §F{f001:Record:priv} (List<i32>:trace, i32:value) -> i32
                §C{trace.Add} §A value §/C
                §R value
              §AF{f002:Get:pub} (List<i32>:trace, bool:first) -> i32
                §S (== §C{Record} §A trace §A result §/C result)
                §IF{i1} first
                  §R INT:5
                §R INT:6
              §AF{f003:Done:pub} (List<i32>:trace) -> void
                §S (== §C{Record} §A trace §A INT:1 §/C INT:1)
                §R
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
            §M{m001:IteratorContracts}
              §F{f001:Values:pub} () -> i32
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
            §M{m001:MethodOwners}
              §CL{c001:Legacy:pub}
                §MT{m001:Pick:pub} (bool:first) -> i32
                  §S (>= result 0)
                  §IF{i1} first
                    §R INT:4
                  §R INT:5
                §MT{m002:op_Implicit:pub:stat} (Legacy:value) -> i32
                  §S (>= result 0)
                  §IF{i2} (!= value null)
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
            §M{m001:OperatorOwner}
              §CL{c001:Box:pub}
                §OP{op001:+:pub}
                  §I{Box:left}
                  §I{Box:right}
                  §O{Box}
                  §S (!= result null)
                  §IF{i1} (!= left null)
                    §R left
                  §R right
                §/OP{op001}
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
            §M{m001:EnumOwner}
              §EN{e001:Color}
              Red
              Blue
              §/EN{e001}
              §EEXT{x001:Color}
                §F{f001:Code:pub} (Color:self, bool:primary) -> i32
                  §S (>= result 0)
                  §IF{i1} primary
                    §R INT:1
                  §R INT:2
              §/EEXT{x001}
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
            §M{m001:LambdaOwner}
              §F{f001:Make:pub} () -> Func<i32>
                §S (== result result)
                §R §LAM{l1} §R INT:42 §/LAM{l1}
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
            §M{m001:MissingReturn}
              §F{f001:Maybe:pub} (bool:take) -> i32
                §S (>= result 0)
                §IF{i1} take
                  §R INT:1
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
            §M{m001:OpaquePreprocessor}
              §F{f001:InBody:pub} () -> i32
                §S (>= result 0)
                §PP{DEBUG}
                  §RAW
                  return 1;
                  §/RAW
                §/PP{DEBUG}
              §F{f002:InElse:pub} () -> i32
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
            §USE{u1:stream:MemoryStream} §NEW{MemoryStream} §/NEW
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
            §W{w1} value
              §K _
                §YIELD INT:1
            """
        },
        {
            "dictionary-foreach",
            """
            §EACHKV{e1:key:item} items
              §YIELD item
            """
        },
        {
            "try",
            """
            §TR{t1}
              §YIELD INT:1
            §/TR{t1}
            """
        },
        {
            "catch",
            """
            §TR{t1}
              §P INT:0
            §CA{Exception:ex}
              §YIELD INT:1
            §/TR{t1}
            """
        },
        {
            "finally",
            """
            §TR{t1}
              §P INT:0
            §FI
              §YIELD INT:1
            §/TR{t1}
            """
        },
        {
            "unsafe",
            """
            §UNSAFE{u1}
              §YIELD INT:1
            §/UNSAFE{u1}
            """
        },
        {
            "fixed",
            """
            §FIXED{f1:pointer:i32*:array}
              §YIELD INT:1
            §/FIXED{f1}
            """
        },
        {
            "sync",
            """
            §SYNC{s1} (gate)
              §YIELD INT:1
            §/SYNC{s1}
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
            §M{m001:NestedYield}
              §F{f001:Values:pub} (i32:value, Dictionary<str,i32>:items, [i32]:array, object:gate) -> i32
                §S (>= result 0)
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
            §M{m001:QuantifiedResult}
              §F{f001:Zero:pub} () -> i32
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
            ".Any(result => (result == 0))",
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
            §M{m001:PatternResult}
              §F{f001:Maybe:pub} (object:fallback) -> object
                §S (|| (is result i32 result) (== result fallback))
                §R fallback
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
            §M{m001:PatternTruthFlow}
              §F{f001:Value:pub} () -> object
                §S (&& (|| (is result i32 result) false) (> result INT:0))
                §R INT:1
            """;

        var assembly = Compile(source, out var generatedCode);
        var value = InvokeStatic(
            assembly,
            "PatternTruthFlowModule",
            "Value");

        Assert.Equal(1, value);
        Assert.Contains(
            "is int result && result > 0",
            generatedCode);
    }

    [Fact]
    public void NegatedPattern_BindsResultOnFalseContinuation()
    {
        const string source = """
            §M{m001:NegatedPattern}
              §F{f001:Value:pub} () -> object
                §S (|| (! (is result i32 result)) (> result INT:0))
                §R INT:1
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
            §M{m001:ContractsOff}
              §RTYPE{r001:NonNegative:i32} (>= # INT:0)
              §F{f001:Iterator:pub} () -> NonNegative
                §S (>= result 0)
                §PP{DEBUG}
                  §YIELD INT:1
                §/PP{DEBUG}
              §F{f002:Opaque:pub} () -> i32
                §S (>= result 0)
                §PP{DEBUG}
                  §RAW
                  return 1;
                  §/RAW
                §/PP{DEBUG}
              §F{f003:Refined:pub} () -> NonNegative
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
            §M{m001:Hygiene}
              §F{f001:ParameterCollision:pub} (i32:__calorPostconditionResult0) -> i32
                §S (>= result 0)
                §LABEL{__calorPostconditionExit0}
                §R __calorPostconditionResult0
              §F{f002:LocalCollision:pub} () -> i32
                §S (>= result 0)
                §B{__calorPostconditionResult1:i32} INT:2
                §LABEL{__calorPostconditionExit1}
                §R __calorPostconditionResult1
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
            §M{m001:PostconditionHygiene}
              §F{f001:Value:pub} () -> object
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
            §M{m001:GenericHygiene}
              §F{f001:Value:pub}<__calorPostconditionResult0> () -> i32
                §S (>= result INT:0)
                §R INT:1
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

    private Assembly Compile(string source, out string generatedCode)
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
        var name = $"PostconditionReturnLowering_{Guid.NewGuid():N}";
        var compilation = CSharpCompilation.Create(
            name,
            [syntaxTree],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(
            emit.Success,
            string.Join(Environment.NewLine, emit.Diagnostics));
        return _assemblies.Load(stream.ToArray(), name);
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
