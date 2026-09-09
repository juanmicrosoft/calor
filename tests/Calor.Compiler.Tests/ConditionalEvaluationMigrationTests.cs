using System.Reflection;
using System.Runtime.Loader;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public class ConditionalEvaluationMigrationTests
{
    [Fact]
    public void FalseAndPostfix_PreservesOriginalResult()
    {
        AssertRoundTrip(
            "int i = 0; bool gate = false; bool ignored = gate && i++ > 0; return i;",
            0, expectInterop: false);
    }

    [Fact]
    public void NestedArithmetic_PreservesLeftToRightCallOrder()
    {
        AssertRoundTrip("int ignored = Trace(1) + Trace(2) * Trace(3); return Calls;",
            123, false, members: """
                private static int Calls;
                public static int Trace(int id) { Calls = Calls * 10 + id; return id; }
                """);
    }

    [Fact]
    public void ArithmeticRightCall_DoesNotChangeAnEarlierOperandValue()
    {
        AssertRoundTrip("int value = 7; int saved = value + Mutate(ref value); return saved;",
            7, false, members: "public static int Mutate(ref int value) { value = 9; return 0; }");
    }

    [Fact]
    public void FieldInitializers_RetainSourceOrder()
    {
        AssertRoundTrip("return Snapshot;", 1, false, members: """
            private static int Seed;
            private static int First = true ? Seed++ : 0;
            private static int Snapshot = Seed;
            """);
    }

    [Theory]
    [InlineData("true", 1)]
    [InlineData("false", 0)]
    public void PreservedFieldOperand_DoesNotMoveItsInitializer(string gate, int expected)
    {
        AssertRoundTrip("return Snapshot;", expected, true, members: $$"""
            private static int Seed;
            private static int First = {{gate}} ? (Seed += 1) : 0;
            private static int Snapshot = Seed;
            """);
    }

    [Fact]
    public void AutoPropertyInitializers_RetainSourceOrder()
    {
        AssertRoundTrip("return Snapshot;", 1, false, members: """
            private static int Seed;
            private static int First { get; } = true ? Seed++ : 0;
            private static int Snapshot { get; } = Seed;
            """);
    }

    [Fact]
    public void ConditionalInvocation_DoesNotBecomeACollidingProperty()
    {
        AssertRoundTrip("var target = new Holder(); int? ignored = target?.M(1); return target.Calls;",
            1, false, members: """
                public sealed class Holder
                {
                    public int Calls;
                    public int M(int value) { Calls++; return value; }
                    public int M1 => 0;
                }
                """);
    }

    [Theory]
    [InlineData("Receiver()", false)]
    [InlineData("((Holder)Receiver())", false)]
    [InlineData("new Holder()", true)]
    [InlineData("Items[0]", true)]
    [InlineData("Items[0].Self", true)]
    public void CapturedReceivers_PreserveNamedArguments(string receiver, bool expectInterop)
    {
        AssertRoundTrip($"bool gate = true; bool ignored = gate && {receiver}.Check(second: 1, first: 2); return Calls;",
            21, expectInterop, members: """
                private static int Calls;
                private static Holder[] Items = new Holder[] { new Holder() };
                public sealed class Holder
                {
                    public Holder Self => this;
                    public bool Check(int first, int second) { Calls = first * 10 + second; return true; }
                }
                public static Holder Receiver() { return new Holder(); }
                """);
    }

    [Theory]
    [InlineData("ref", "false", 7)]
    [InlineData("ref", "true", 9)]
    [InlineData("out", "false", 7)]
    [InlineData("out", "true", 9)]
    public void CapturedReceivers_PreserveWritableArgumentModifiers(string modifier, string gate, int expected)
    {
        AssertRoundTrip($"int value = 7; bool gate = {gate}; bool ignored = gate && Receiver().Check({modifier} value); return value;",
            expected, false, members: $$"""
                public sealed class Holder
                {
                    public bool Check({{modifier}} int value) { value = 9; return true; }
                }
                public static Holder Receiver() { return new Holder(); }
                """);
    }

    [Theory]
    [InlineData("false", 0)]
    [InlineData("true", 7)]
    public void CapturedReceivers_PreserveInArgumentModifier(string gate, int expected)
    {
        AssertRoundTrip($"int value = 7; bool gate = {gate}; bool ignored = gate && Receiver().Check(in value); return Calls;",
            expected, false, members: """
                private static int Calls;
                public sealed class Holder
                {
                    public bool Check(in int value) { Calls = value; return true; }
                }
                public static Holder Receiver() { return new Holder(); }
                """);
    }

    [Theory]
    [InlineData("false", 2)]
    [InlineData("true", 5)]
    public void CapturedReceivers_CombineNamesAndModifiers(string gate, int expected)
    {
        AssertRoundTrip(
            $"int first = 2; int second = 3; bool gate = {gate}; bool ignored = gate && Receiver().Check(second: in second, first: ref first); return first;",
            expected, false, members: """
                public sealed class Holder
                {
                    public bool Check(ref int first, in int second) { first += second; return true; }
                }
                public static Holder Receiver() { return new Holder(); }
                """);
    }

    [Theory]
    [InlineData("ref")]
    [InlineData("out")]
    public void StatementCalls_PreserveNamedWritableArguments(string modifier)
    {
        AssertRoundTrip($"int value = 0; Set(value: {modifier} value); return value;",
            9, false, members: $"public static void Set({modifier} int value) {{ value = 9; }}");
    }

    [Theory]
    [InlineData("true", 9)]
    [InlineData("false", 7)]
    public void ConditionalStatements_SelectTheRefOverload(string gate, int expected)
    {
        AssertRoundTrip(
            $"int value = 7; Holder target = {gate} ? new Holder() : null; target?.Check(ref value); return value;",
            expected, false, members: """
                public sealed class Holder
                {
                    public void Check(ref int value) { value = 9; }
                    public void Check(int value) { }
                }
                """);
    }

    [Theory]
    [InlineData("ref", "true", 9)]
    [InlineData("ref", "false", 7)]
    [InlineData("out", "true", 9)]
    [InlineData("out", "false", 7)]
    public void ConditionalInterpolation_PreservesWritableOverload(string modifier, string gate, int expected)
    {
        AssertRoundTrip(
            $"int value = 7; bool gate = {gate}; string ignored = gate ? $\"{{Update(value: {modifier} value)}}\" : \"\"; return value;",
            expected, false, members: $$"""
                public static int Update({{modifier}} int value) { value = 9; return 0; }
                public static int Update(int value) { return 0; }
                """, expectEmitterFallback: true);
    }

    [Theory]
    [InlineData("true", 7)]
    [InlineData("false", 0)]
    public void ConditionalInterpolation_PreservesInOverload(string gate, int expected)
    {
        AssertRoundTrip(
            $"int value = 7; bool gate = {gate}; string ignored = gate ? $\"{{Update(in value)}}\" : \"\"; return Calls;",
            expected, false, members: """
                private static int Calls;
                public static int Update(in int value) { Calls = value; return 0; }
                public static int Update(int value) { return 0; }
                """, expectEmitterFallback: true);
    }

    [Theory]
    [InlineData("§C{Use} §A{readonly} value §/C")]
    [InlineData("§C{Use} §A{ref:out} value §/C")]
    [InlineData("§C{Use} §A{} value §/C")]
    [InlineData("§B{result} §C §C{Get} §/C §A{ref} value §/C")]
    [InlineData("§B{result} §NEW{Box} §A{ref} value")]
    public void UnsupportedArgumentModifiers_AreDiagnosed(string statement)
    {
        var source = $$"""
            §M{m:Probe}
              §F{f:Run:pub} () -> void
                {{statement}}
            """;
        var diagnostics = new DiagnosticBag();
        new Calor.Compiler.Parsing.Parser(
            new Calor.Compiler.Parsing.Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.InvalidModifier);
    }

    [Theory]
    [InlineData("true", "(+ n 1)", 3)]
    [InlineData("false", "(+ n 1)", 0)]
    [InlineData("true", "§C{Size} §/C", 3)]
    [InlineData("false", "§C{Size} §/C", 0)]
    public void NativeConditionalArraySizes_SurvivePrettyRoundTrip(string gate, string size, int expected)
    {
        var source = $$"""
            §M{m:Probe}
              §F{s:Size:pub} () -> i32
                §R 3
              §F{r:Run:pub} () -> i32
                §B{n:i32} 2
                §B{values} (? {{gate}} §ARR{a:i32:"{{size}}"} §ARR{b:i32:0})
                §R (len values)
            """;
        var options = new Calor.Compiler.CompilationOptions { EnforceEffects = false, StatusWriter = TextWriter.Null };
        var first = Program.Compile(source, "native-array.calr", options);
        Assert.False(first.HasErrors, string.Join(Environment.NewLine, first.Diagnostics.Errors));
        Assert.Equal(expected, Execute(first.GeneratedCode));
        var diagnostics = new DiagnosticBag();
        var parser = new Calor.Compiler.Parsing.Parser(
            new Calor.Compiler.Parsing.Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics);
        var pretty = new CalorEmitter().Emit(parser.Parse());
        Assert.DoesNotContain("§CS", pretty);
        var second = Program.Compile(pretty, "native-array-pretty.calr", options);
        Assert.False(second.HasErrors, string.Join(Environment.NewLine, second.Diagnostics.Errors));
        Assert.Equal(expected, Execute(second.GeneratedCode));
    }

    [Theory]
    [InlineData("true && i++ > 0", 1)]
    [InlineData("false && i++ > 0", 0)]
    [InlineData("true || i++ > 0", 0)]
    [InlineData("false || i++ > 0", 1)]
    [InlineData("false && (i++ > 0 || ++i > 0)", 0)]
    [InlineData("true && (i++ > 0 || ++i > 0)", 2)]
    public void LogicalOperands_PreserveEvaluationCount(string expression, int expected)
    {
        AssertRoundTrip($"int i = 0; bool ignored = {expression}; return i;", expected, false);
    }

    [Theory]
    [InlineData("false && Tick()", 0)]
    [InlineData("true && Tick()", 1)]
    [InlineData("true || Tick()", 0)]
    [InlineData("false || Tick()", 1)]
    public void NativeCalls_StayInsideLazyOperands(string expression, int expected)
    {
        AssertRoundTrip($"bool ignored = {expression}; return Calls;", expected, false,
            members: "private static int Calls; public static bool Tick() { Calls++; return true; }");
    }

    [Theory]
    [InlineData("false && Tick()", 0)]
    [InlineData("true && Tick()", 1)]
    [InlineData("true || Tick()", 0)]
    [InlineData("false || Tick()", 1)]
    public void IfConditions_DoNotStageIndividualLazyCalls(string condition, int expected)
    {
        AssertRoundTrip($"if ({condition}) return Calls; return Calls;", expected, false,
            members: "private static int Calls; public static bool Tick() { Calls++; return true; }");
    }

    [Theory]
    [InlineData("false", 0)]
    [InlineData("true", 123)]
    public void ChainedReceiver_StaysBeforeArgumentsInsideSelectedOperand(string gate, int expected)
    {
        AssertRoundTrip($"bool gate = {gate}; bool ignored = gate && Receiver().Check(Argument()); return Calls;",
            expected, false, members: """
                private static int Calls;
                public sealed class Holder
                {
                    public bool Check(int value) { Calls = Calls * 10 + 3; return true; }
                }
                public static Holder Receiver() { Calls = Calls * 10 + 1; return new Holder(); }
                public static int Argument() { Calls = Calls * 10 + 2; return 0; }
                """);
    }

    [Theory]
    [InlineData("false", 0)]
    [InlineData("true", 12)]
    public void NullReceiver_CaptureDoesNotSkipArgumentEvaluation(string gate, int expected)
    {
        AssertRoundTrip(
            $"bool gate = {gate}; try {{ bool ignored = gate && Receiver().Check(Argument()); }} catch (System.NullReferenceException) {{ return Calls; }} return Calls;",
            expected, false, members: """
                private static int Calls;
                public sealed class Holder { public bool Check(int value) { return true; } }
                public static Holder Receiver() { Calls = Calls * 10 + 1; return null; }
                public static int Argument() { Calls = Calls * 10 + 2; return 0; }
                """);
    }

    [Theory]
    [InlineData("false", 0)]
    [InlineData("true", 12)]
    public void ArrayArguments_StayInsideSelectedOperand(string gate, int expected)
    {
        AssertRoundTrip($"bool gate = {gate}; bool ignored = gate && Check(new int[] {{ First(), Second() }}); return Calls;",
            expected, false, members: """
                private static int Calls;
                public static int First() { Calls = Calls * 10 + 1; return 1; }
                public static int Second() { Calls = Calls * 10 + 2; return 2; }
                public static bool Check(int[] values) { return values.Length == 2; }
                """);
    }

    [Theory]
    [InlineData("false", 0)]
    [InlineData("true", 2)]
    public void ExpressionLambda_KeepsTargetTypingAndConditionalInvocation(string gate, int expected)
    {
        AssertRoundTrip($"bool gate = {gate}; bool ignored = gate && Apply(value => value + 1); return Calls;",
            expected, false, members: """
                private static int Calls;
                public static bool Apply(System.Func<int, int> callback) { Calls = callback(1); return true; }
                """);
    }

    [Theory]
    [InlineData("false", 1)]
    [InlineData("true", 2)]
    public void DynamicArraySize_IsPreservedWithinItsOperand(string gate, int expected)
    {
        AssertRoundTrip($"int i = 1; bool gate = {gate}; bool ignored = gate && Check(new int[i++]); return i;",
            expected, true, members: "public static bool Check(int[] values) { return values.Length > 0; }");
    }

    [Theory]
    [InlineData("false && Fail()", 0)]
    [InlineData("true && Fail()", 123)]
    [InlineData("true || Fail()", 0)]
    [InlineData("false || Fail()", 123)]
    public void NativeCalls_PreserveSkippedExceptions(string expression, int expected)
    {
        AssertRoundTrip(
            $"try {{ bool ignored = {expression}; }} catch (System.InvalidOperationException) {{ return 123; }} return 0;",
            expected, false,
            members: "public static bool Fail() { throw new System.InvalidOperationException(); }");
    }

    [Theory]
    [InlineData("7", 0)]
    [InlineData("null", 1)]
    public void Coalescing_PreservesConditionalIncrement(string value, int expected)
    {
        AssertRoundTrip($"int i = 0; int? value = {value}; int ignored = value ?? i++; return i;",
            expected, false);
    }

    [Theory]
    [InlineData("true", 12)]
    [InlineData("false", 22)]
    public void ConditionalArms_PreserveOrderAndResult(string gate, int expected)
    {
        AssertRoundTrip($"int i = 1; bool gate = {gate}; int value = gate ? i++ : ++i; return value * 10 + i;",
            expected, false);
    }

    [Theory]
    [InlineData("null", 0)]
    [InlineData("new int[] { 42 }", 1)]
    public void ConditionalIndex_PreservesSkippedIndex(string target, int expected)
    {
        AssertRoundTrip($"int i = 0; int[] target = {target}; int? ignored = target?[i++]; return i;",
            expected, true);
    }

    [Theory]
    [InlineData("false && 1 / i++ > 0", 0)]
    [InlineData("true && 1 / i++ > 0", 101)]
    [InlineData("true || 1 / i++ > 0", 0)]
    [InlineData("false || 1 / i++ > 0", 101)]
    public void LogicalOperands_PreserveExceptions(string expression, int expected)
    {
        AssertRoundTrip(
            $"int i = 0; try {{ bool ignored = {expression}; }} catch (System.DivideByZeroException) {{ return 100 + i; }} return i;",
            expected, false);
    }

    [Theory]
    [InlineData("7", 0)]
    [InlineData("null", 101)]
    public void Coalescing_PreservesExceptions(string value, int expected)
    {
        AssertRoundTrip(
            $"int i = 0; int? value = {value}; try {{ int ignored = value ?? 1 / i++; }} catch (System.DivideByZeroException) {{ return 100 + i; }} return i;",
            expected, false);
    }

    [Theory]
    [InlineData("true", 101)]
    [InlineData("false", 0)]
    public void ConditionalArms_PreserveExceptions(string gate, int expected)
    {
        AssertRoundTrip(
            $"int i = 0; bool gate = {gate}; try {{ int ignored = gate ? 1 / i++ : 42; }} catch (System.DivideByZeroException) {{ return 100 + i; }} return i;",
            expected, false);
    }

    [Theory]
    [InlineData("null", 2)]
    [InlineData("new int[] { 42 }", 103)]
    public void ConditionalIndex_PreservesExceptions(string target, int expected)
    {
        AssertRoundTrip(
            $"int i = 2; int[] target = {target}; try {{ int? ignored = target?[i++]; }} catch (System.IndexOutOfRangeException) {{ return 100 + i; }} return i;",
            expected, true);
    }

    [Theory]
    [InlineData("null", 0)]
    [InlineData("\"abc\"", 1)]
    public void ConditionalCall_PreservesSkippedArguments(string target, int expected)
    {
        AssertRoundTrip(
            $"int i = 0; string target = {target}; string ignored = target?.Substring(i++); return i;",
            expected, false);
    }

    [Theory]
    [InlineData("null", 4)]
    [InlineData("\"abc\"", 105)]
    public void ConditionalCall_PreservesExceptions(string target, int expected)
    {
        AssertRoundTrip(
            $"int i = 4; string target = {target}; try {{ string ignored = target?.Substring(i++); }} catch (System.ArgumentOutOfRangeException) {{ return 100 + i; }} return i;",
            expected, false);
    }

    [Theory]
    [InlineData("null", 0)]
    [InlineData("\"abc\"", 1)]
    public void ConditionalCallStatement_PreservesSkippedArguments(string target, int expected)
    {
        AssertRoundTrip($"int i = 0; string target = {target}; target?.Substring(i++); return i;",
            expected, false);
    }

    [Fact]
    public void ConditionalCallStatement_EvaluatesPropertyReceiverOnce()
    {
        AssertRoundTrip("Target?.ToString(); return Calls;", 1, false,
            members: "private static int Calls; public static string Target { get { Calls++; return \"abc\"; } }");
    }

    [Theory]
    [InlineData("null", 0)]
    [InlineData("\"abc\"", 1)]
    public void ConditionalChains_KeepLaterArgumentsLazy(string target, int expected)
    {
        AssertRoundTrip(
            $"int i = 0; string target = {target}; int? ignored = target?.Trim().Substring(i++).Length; return i;",
            expected, false);
    }

    [Theory]
    [InlineData("null", 0)]
    [InlineData("\"abc\"", 1)]
    public void RepeatedConditionalAccess_KeepsArgumentsLazy(string target, int expected)
    {
        AssertRoundTrip(
            $"int i = 0; string target = {target}; int? ignored = target?.Trim()?.Substring(i++)?.Length; return i;",
            expected, false);
    }

    [Theory]
    [InlineData("42", 0)]
    [InlineData("\"abc\"", 1)]
    public void DeclarationPattern_DoesNotCastBeforeMatching(string value, int expected)
    {
        AssertRoundTrip(
            $"object value = {value}; bool matches = value is string text && text.Length > 0; return matches ? 1 : 0;",
            expected, false);
    }

    [Theory]
    [InlineData("true", 5)]
    [InlineData("false", 0)]
    public void OutDeclaration_RemainsAssignedOnlyBySelectedOperand(string gate, int expected)
    {
        AssertRoundTrip(
            $"bool gate = {gate}; if (gate && int.TryParse(\"5\", out int value)) return value; return 0;",
            expected, true);
    }

    [Theory]
    [InlineData("inc", "++i", 1)]
    [InlineData("dec", "--i", -1)]
    [InlineData("post-inc", "i++", 1)]
    [InlineData("post-dec", "i--", -1)]
    public void NativeUnaryMutations_StayInTheirBranch(string operation, string expression, int expected)
    {
        AssertRoundTrip($"int i = 0; bool ignored = true && {expression} == 0; return i;", expected, false);
        var node = new Calor.Compiler.Ast.UnaryOperationNode(default,
            Calor.Compiler.Ast.UnaryOperatorExtensions.FromString(operation)!.Value,
            new Calor.Compiler.Ast.ReferenceNode(default, "i"));
        Assert.Equal($"({operation} i)", new CalorEmitter().Visit(node));
    }

    [Theory]
    [InlineData("\"yes\"", 0)]
    [InlineData("null", 101)]
    public void CoalescingThrow_PreservesExceptionConstruction(string value, int expected)
    {
        AssertRoundTrip(
            $"int i = 0; string value = {value}; try {{ string ignored = value ?? throw new System.Exception((i++).ToString()); }} catch (System.Exception) {{ return 100 + i; }} return i;",
            expected, false);
    }

    [Theory]
    [InlineData("true", 0)]
    [InlineData("false", 101)]
    public void ConditionalThrow_PreservesExceptionConstruction(string gate, int expected)
    {
        AssertRoundTrip(
            $"int i = 0; bool gate = {gate}; try {{ int ignored = gate ? 42 : throw new System.Exception((i++).ToString()); }} catch (System.Exception) {{ return 100 + i; }} return i;",
            expected, false);
    }

    [Theory]
    [InlineData(ConversionMode.Standard)]
    [InlineData(ConversionMode.Interop)]
    public void ConditionalOperandInteropIsCountedInEveryMode(ConversionMode mode)
    {
        AssertRoundTrip(
            "int i = 0; bool ignored = false && (i += 1) > 0; return ignored ? 9 : i;",
            0, true, mode);
    }

    [Fact]
    public void PreservedConditionalOperand_DoesNotLeakEarlierPreludes()
    {
        AssertRoundTrip("return Other();", 0, true,
            members: "public static int Other() { int i = 0; int[] target = null; return i++ + (target?[i++] ?? 0); }");
    }

    [Theory]
    [InlineData("int i = 0; int saved = i++; return saved * 10 + i;", 1)]
    [InlineData("int i = 1; bool gate = true; bool ignored = gate && i > 0; return i;", 1)]
    [InlineData("int i = 0; bool gate = true; int value = i++ + (gate ? 7 : 9); return value * 10 + i;", 71)]
    public void AlreadyFaithfulExpressions_RemainNative(string body, int expected)
    {
        AssertRoundTrip(body, expected, expectInterop: false);
    }

    private static void AssertRoundTrip(
        string body, int expected, bool expectInterop,
        ConversionMode mode = ConversionMode.Standard, string members = "", bool expectEmitterFallback = false)
    {
        var original = $$"""
            public static class Probe
            {
                {{members}}
                public static int Run()
                {
                    {{body}}
                }
            }
            """;
        Assert.Equal(expected, Execute(original));
        var conversion = new CSharpToCalorConverter(new ConversionOptions { Mode = mode }).Convert(original);
        Assert.True(conversion.Success, string.Join(Environment.NewLine, conversion.Issues));
        Assert.NotNull(conversion.CalorSource);
        // Migration does not synthesize effect declarations; keep the production
        // type checker and generated-C# backstop, but do not require effect rows.
        var compilation = Program.Compile(conversion.CalorSource, "conditional-migration.calr",
            new Calor.Compiler.CompilationOptions { EnforceEffects = false, StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics.Errors));
        var actual = Execute(compilation.GeneratedCode);
        Assert.True(expected == actual,
            $"Expected {expected}, actual {actual}\n{conversion.CalorSource}\n{compilation.GeneratedCode}");
        Assert.True(expectInterop ==
            conversion.Losses.Any(loss => loss.Kind == ConversionLossKind.InteropPreserved),
            $"Unexpected interop accounting:\n{string.Join(Environment.NewLine, conversion.Issues)}\n{conversion.CalorSource}");
        Assert.Equal(expectEmitterFallback,
            conversion.Losses.Any(loss => loss.Kind == ConversionLossKind.EmitterFallback));
        if (expectInterop)
        {
            Assert.Contains(conversion.Issues, issue =>
                issue.Feature is "conditional-expression-hoisting" or "conditional-access-shape");
        }
    }

    private static int Execute(string source)
    {
        var compilation = CSharpCompilation.Create("ConditionalMigration_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + source)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext(compilation.AssemblyName!, isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(stream);
            var run = assembly.GetTypes().SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Single(method => method.Name == "Run");
            return Assert.IsType<int>(run.Invoke(null, null));
        }
        finally
        {
            context.Unload();
        }
    }
}
