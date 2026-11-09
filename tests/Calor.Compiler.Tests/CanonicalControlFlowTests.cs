using System.Reflection;
using System.Text;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class CanonicalControlFlowTests
{
    [Fact]
    public void DictionaryForeach_QualifiedNativeParameterPreservesElementTypes()
    {
        const string source = """
            module Pairs
              func Probe public (System.Collections.Generic.Dictionary<string,int>:pairs) → int
                var int:total = 0
                each key, value in pairs
                  let int:count = value
                  set total total + count
                return total
            """;
        foreach (var program in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var type = Load(Compile(program).GeneratedCode);
            Assert.Equal(7, type.GetMethod("Probe")!.Invoke(null,
                [new Dictionary<string, int> { ["a"] = 3, ["b"] = 4 }]));
        }
    }

    [Theory]
    [InlineData("for i from 0 to 3 step 1", "set n n + i", "6")]
    [InlineData("for i from 3 to 0 step -1", "set n n + i", "6")]
    [InlineData("for i from 3 to 0 step 1", "set n n + 1", "0")]
    [InlineData("for i from 0 to 3 step -1", "set n n + 1", "0")]
    [InlineData("for i from 0 to 3 step 0", "set n n + 1", "throws:ArgumentOutOfRangeException")]
    [InlineData("for i from 3 to 0 step 0", "set n n + 1", "throws:ArgumentOutOfRangeException")]
    [InlineData("for i from 2147483646 to 2147483647 step 2", "set n n + 1", "1")]
    [InlineData("for i from -2147483647 to -2147483648 step -2", "set n n + 1", "1")]
    public void SnapshotRange_PreservesDirectionZeroIterationAndOverflow(string header, string body, string expected)
        => AssertObservation($"var int:n = 0\n{header}\n  {body}\nreturn n", expected);

    [Fact]
    public void SnapshotRange_EvaluatesHeaderOnceAndContinueStillSteps()
        => AssertObservation("""
            var int:n = 0
            for i from Start() to Bound() step Step()
              if i == 1
                continue
              set n n + i
            return n
            """, "5:ABS", """
            public static string Trace = "";
            private static int Start() { Trace += "A"; return 0; }
            private static int Bound() { Trace += "B"; return 3; }
            private static int Step() { Trace += "S"; return 1; }
            """);

    [Theory]
    [InlineData("checked", "9223372036854775806", "9223372036854775807", "2")]
    [InlineData("unchecked", "9223372036854775806", "9223372036854775807", "2")]
    [InlineData("checked", "-9223372036854775807", "-9223372036854775807", "-2")]
    [InlineData("unchecked", "-9223372036854775807", "-9223372036854775807", "-2")]
    public void SnapshotRange_TypedLongBinderTerminatesAtOverflowInEitherModulePolicy(
        string overflow, string start, string end, string step)
        => AssertObservation($$"""
            var int:n = 0
            for long:item from {{start}} to {{end}} step {{step}}
              set n n + 1
              if n == 3
                break
            return n
            """, "1", overflow: overflow);

    [Theory]
    [InlineData("for @step from 1 to 3 step 1")]
    [InlineData("for @step = 1; @step <= 3; @step = @step + 1")]
    public void For_KeywordBinderRetainsSemanticNameAndRoundTrips(string header)
        => AssertObservation($"var int:n = 0\n{header}\n  set n n + @step\nreturn n", "6");

    [Fact]
    public void Each_KeywordBinderRetainsSemanticNameAndRoundTrips()
        => AssertObservation("""
            var int:n = 0
            each @while in new int[] {1, 2, 3}
              set n n + @while
            return n
            """, "6", effectCodes: "alloc");

    [Fact]
    public void EachDeconstruction_KeywordBindersRetainSemanticNamesAndRoundTrip()
        => AssertObservation("""
            var int:n = 0
            each @for, @in in Pairs()
              set n n + @for + @in
            return n
            """, "11", """
            public static string Trace = "";
            private static System.Collections.Generic.IEnumerable<(int, int)> Pairs()
            {
                yield return (1, 10);
            }
            """);

    [Fact]
    public void ReEvaluatedFor_PreservesTestUpdateContinueAndFinalFailingTest()
        => AssertObservation("""
            var int:n = 0
            for i = Start(); i < Bound(); i = Next(i)
              if i == 1
                continue
              set n n + i
            return n
            """, "2:ABSBSBSB", """
            public static string Trace = "";
            private static int Start() { Trace += "A"; return 0; }
            private static int Bound() { Trace += "B"; return 3; }
            private static int Next(int i) { Trace += "S"; return i + 1; }
            """);

    [Fact]
    public void ReEvaluatedFor_BreakDoesNotExecuteUpdateOrTestAgain()
        => AssertObservation("""
            var int:n = 0
            for i = 0; i < Bound(); i = Next(i)
              set n n + 1
              break
            return n
            """, "1:B", """
            public static string Trace = "";
            private static int Bound() { Trace += "B"; return 3; }
            private static int Next(int i) { Trace += "S"; return i + 1; }
            """);

    [Fact]
    public void ReEvaluatedFor_AllowsBodyMutationAndInvocationUpdate()
        => AssertObservation("""
            var int:n = 0
            for i = 0; i < 3; Tick()
              set i i + 1
              set n n + i
            return n
            """, "6:TTT", """
            public static string Trace = "";
            private static void Tick() { Trace += "T"; }
            """);

    [Theory]
    [InlineData("checked", "throws:OverflowException")]
    [InlineData("unchecked", "1")]
    public void ReEvaluatedFor_PreservesOverflowPolicy(string overflow, string expected)
        => AssertObservation("""
            var int:n = 0
            for i = 2147483647; n < 2; i = i + 1
              if i < 0
                return 1
              set n n + 1
            return 0
            """, expected, overflow: overflow);

    [Fact]
    public void ReEvaluatedFor_UpdateComputedStoreKeepsOperandOrder()
        => AssertObservation("""
            var int:n = 0
            for i = 0; i < 2; Store()[Key()] = Value()
              set i i + 1
              set n n + 1
            return n
            """, "2:AKVAKV", """
            public static string Trace = "";
            public static int[] Values = new int[1];
            private static int[] Store() { Trace += "A"; return Values; }
            private static int Key() { Trace += "K"; return 0; }
            private static int Value() { Trace += "V"; return 1; }
            """);

    [Fact]
    public void NativeSet_ComputedStoreEvaluatesReceiverIndexAndValueOnce()
        => AssertObservation("""
            set Store()[Key()] Value()
            return 1
            """, "1:AKV", """
            public static string Trace = "";
            public static int[] Values = new int[1];
            private static int[] Store() { Trace += "A"; return Values; }
            private static int Key() { Trace += "K"; return 0; }
            private static int Value() { Trace += "V"; return 1; }
            """);

    [Fact]
    public void NativeDo_PreservesUnassignedLocalAndFirstIteration()
        => AssertObservation("""
            var int:result
            var int:i = 0
            do
              set result i
              set i i + 1
            while i < 2
            return result
            """, "1");

    [Fact]
    public void ReEvaluatedFor_ContinueAndBreakRunFinallyBeforeTheNextLoopAction()
        => AssertObservation("""
            var int:n = 0
            for i = 0; i < 3; i = i + 1
              try
                if i == 1
                  continue
                if i == 2
                  break
                set n n + 1
              finally
                set n n + 10
            return n
            """, "31");

    [Fact]
    public void NativeDo_ContinueReachesTrailingTestAndDoesNotSkipFirstIteration()
        => AssertObservation("""
            var int:n = 0
            do
              set n n + 1
              continue
            while n < 3
            return n
            """, "3");

    [Fact]
    public void NativeWhile_AndIfElseRetainBodyOrder()
        => AssertObservation("""
            var int:n = 0
            var int:sum = 0
            while n < 4
              set n n + 1
              if n % 2 == 0
                set sum sum + n
              else
                set sum sum - n
            return sum
            """, "2");

    [Fact]
    public void TryCatchFilterFinally_PreservesFilterOrderAndExceptionIdentity()
        => AssertObservation("""
            var int:n = 0
            try
              Fail()
            catch System.InvalidOperationException:first when Filter(1)
              set n 10
            catch System.InvalidOperationException:second when Filter(2)
              set n 20
            finally
              Release()
            return n
            """, "20:X12Z", """
            public static string Trace = "";
            private static void Fail() { Trace += "X"; throw new System.InvalidOperationException(); }
            private static bool Filter(int n) { Trace += n.ToString(); return n == 2; }
            private static void Release() { Trace += "Z"; }
            """, effectCodes: "mut,throw");

    [Fact]
    public void Using_ResourceExpressionRunsOnceAndDisposesOnReturn()
        => AssertObservation("""
            using Acquire()
              return 1
            """, "1:AD", """
            public static string Trace = "";
            private static Resource Acquire()
            {
                Trace += "A";
                return new Resource();
            }
            """, declarations: NativeDisposable(), effectCodes: "mut,alloc");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IndexedStoreVisitors_EmitNativeSetAndEvaluateOperandsOnce(bool dictionaryPut)
    {
        var span = new TextSpan(0, 0, 1, 1);
        var index = new CallExpressionNode(span, "Index", []);
        var value = new CallExpressionNode(span, "Value", []);
        StatementNode store = dictionaryPut
            ? new DictionaryPutNode(span, "items", index, value)
            : new CollectionSetIndexNode(span, "items", index, value);
        var function = new FunctionNode(span, "f001", "Store", Visibility.Public, [],
            null, null, [store], new AttributeCollection());
        var module = new ModuleNode(span, "m001", "Stores", [], [function], new AttributeCollection());
        var emitted = new CalorEmitter().Emit(module).Split('\n')
            .Single(line => line.TrimStart().StartsWith("set ", StringComparison.Ordinal)).Trim();
        Assert.Equal("set items[Index()] Value()", emitted);
        AssertObservation("var int[]:items = new int[] { 0 }\n" + emitted + "\nreturn items[0]",
            "7:IV", """
            public static string Trace = "";
            private static int Index() { Trace += "I"; return 0; }
            private static int Value() { Trace += "V"; return 7; }
            """, effectCodes: "mut,alloc");
    }

    private static string NativeDisposable()
    {
        const string payload = "Instrumentation.Trace += \"D\";";
        return "  class Resource\n    implements System.IDisposable\n    func Dispose () → void\n" +
            $"      ! mut\n      raw statement {Encoding.UTF8.GetByteCount(payload)}\n{payload}\n";
    }

    [Fact]
    public void Using_ReturnedNativeResourceStillChargesDisposeEffects()
    {
        const string source = """
            module DisposalEffects
              class Resource
                implements System.IDisposable
                field int:Calls static
                func Dispose public ()
                  ! mut
                  set Calls Calls + 1
              func Acquire () → Resource
                ! alloc
                return new Resource()
              func Probe () → int
                ! alloc
                using Acquire()
                  return 1
            """;
        var result = Program.Compile(source, "using-effects.calr");
        Assert.Contains(result.Diagnostics.Errors, diagnostic =>
            diagnostic.Message.Contains("'Probe'", StringComparison.Ordinal)
            && diagnostic.Message.Contains("'mut'", StringComparison.Ordinal));
    }

    [Fact]
    public void PrimitiveThisFieldCompound_ReadsBeforeTheMutatingRightOperand()
    {
        const string csharp = """
            public class Counters
            {
                private int count;
                private int Next() { count = 10; return 1; }
                private void Add() { count += Next(); }
                public static int Probe()
                {
                    var counter = new Counters();
                    counter.Add();
                    return counter.count;
                }
            }
            """;
        var conversion = new CSharpToCalorConverter().Convert(csharp);
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.Empty(conversion.Losses);
        Assert.DoesNotContain("raw ", conversion.CalorSource);
        Assert.DoesNotContain('§', conversion.CalorSource!);
        var type = Load(Compile(conversion.CalorSource!).GeneratedCode);
        Assert.Equal(1, type.GetMethod("Probe")!.Invoke(null, null));
    }

    [Fact]
    public void Lock_ResourceExpressionRunsOnce()
        => AssertObservation("""
            var int:n = 0
            lock Gate()
              set n 1
            return n
            """, "1:G", """
            public static string Trace = "";
            public static object GateValue = new object();
            private static object Gate() { Trace += "G"; return GateValue; }
            """);

    [Fact]
    public void UnsafeFixed_RetainsTheNativeFixedBlock()
        => AssertObservation("""
            var int:n = 0
            unsafe
              fixed byte*:p = new byte[] {1}
                set n 1
            return n
            """, "1", effectCodes: "alloc,unsafe");

    [Fact]
    public void BlockMatch_ExecutesOnlyTheSelectedArm()
        => AssertObservation("""
            var int:n = 2
            var int:result = 0
            match n
              case 0
                set result 10
              case 2
                set result 20
              case _
                set result 30
            return result
            """, "20");

    [Theory]
    [InlineData(false, "GHAGHR")]
    [InlineData(true, "GHNGHN")]
    public void EventNodes_EvaluateReceiverThenHandlerOnceWithoutReadingTheEvent(
        bool nullReceiver, string expected)
    {
        var target = new FieldAccessNode(default, new CallExpressionNode(default, "Source", []), "Changed");
        var handler = new CallExpressionNode(default, "MakeHandler", []);
        var subscribe = new EventSubscribeNode(default, target, handler).Accept(new CSharpEmitter());
        var unsubscribe = new EventUnsubscribeNode(default, target, handler).Accept(new CSharpEmitter());
        var source = $$"""
            public class EventProbe
            {
                private static string Trace = "";
                public event System.Action Changed
                {
                    add { Trace += "A"; }
                    remove { Trace += "R"; }
                }
                private static EventProbe Source()
                {
                    Trace += "G";
                    return {{(nullReceiver ? "null" : "new EventProbe()")}};
                }
                private static System.Action MakeHandler() { Trace += "H"; return Handle; }
                private static void Handle() { }
                public static string Probe()
                {
                    try { {{subscribe}} } catch (System.NullReferenceException) { Trace += "N"; }
                    try { {{unsubscribe}} } catch (System.NullReferenceException) { Trace += "N"; }
                    return Trace;
                }
            }
            """;
        var type = Load(source);
        Assert.Equal(expected, type.GetMethod("Probe")!.Invoke(null, null));
    }

    [Fact]
    public void EventProbeEnvelope_PreservesAccessorSemanticsWithExplicitStatementInterop()
    {
        const string source = """
            module EventProbe
              class Hub
                field int:Calls public = 0
                event System.Action:Changed
                  add
                    set Calls Calls + 1
                  remove
                    set Calls Calls + 10
                func Handle private () → void
                  return
                func Source private () → Hub
                  return this
                func MakeHandler private () → System.Action
                  return Handle
                func Probe () → int
                  ! mut
                  let System.Action:handler = Handle
                  raw statement 19
            Changed += handler;
                  raw statement 19
            Changed -= handler;
                  raw statement 34
            Source().Changed += MakeHandler();
                  raw statement 34
            Source().Changed -= MakeHandler();
                  return Calls
            """;
        foreach (var program in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var type = Load(Compile(program).GeneratedCode);
            Assert.Equal(22, type.GetMethod("Probe")!.Invoke(Activator.CreateInstance(type), null));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PrintVisitor_EmitsNativeConsoleCallAndEvaluatesItsOperandOnce(bool writeLine)
    {
        var span = new TextSpan(0, 0, 1, 1);
        var statement = new PrintStatementNode(span, new CallExpressionNode(span, "Next", []), writeLine);
        var function = new FunctionNode(span, "f001", "Store", Visibility.Public, [],
            null, null, [statement], new AttributeCollection());
        var module = new ModuleNode(span, "m001", "Printing", [], [function], new AttributeCollection());
        var emitted = new CalorEmitter().Emit(module).Split('\n')
            .Single(line => line.TrimStart().StartsWith("System.Console.", StringComparison.Ordinal)).Trim();
        Assert.Equal($"System.Console.{(writeLine ? "WriteLine" : "Write")}(Next())", emitted);
        AssertObservation(emitted + "\nreturn Count()", "1", """
            public static int Calls;
            private static int Next() { Calls++; return 42; }
            private static int Count() { return Calls; }
            """, effectCodes: "mut,cw");
    }

    [Theory]
    [InlineData("", 13)]
    [InlineData("FIRST", 11)]
    [InlineData("SECOND", 12)]
    [InlineData("FIRST,SECOND", 11)]
    public void StatementPreprocessor_PreservesEveryBranchAndFollowingSibling(string symbols, int expected)
    {
        const string source = """
            module Conditions
              func Probe () → int
                var int:n = 0
                pp FIRST
                  set n 1
                pp elif SECOND
                  set n 2
                pp else
                  set n 3
                set n n + 10
                return n
            """;
        foreach (var program in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var type = Load(Compile(program).GeneratedCode,
                symbols.Split(',', StringSplitOptions.RemoveEmptyEntries));
            Assert.Equal(expected, type.GetMethod("Probe")!.Invoke(null, null));
        }
    }

    [Theory]
    [InlineData("", 3)]
    [InlineData("OUTER", 2)]
    [InlineData("OUTER,INNER", 1)]
    [InlineData("INNER", 3)]
    public void StatementPreprocessor_NestedElseStaysWithItsOwner(string symbols, int expected)
    {
        const string source = """
            module Conditions
              func Probe () → int
                var int:n = 0
                pp OUTER
                  pp INNER
                    set n 1
                  pp else
                    set n 2
                pp else
                  set n 3
                return n
            """;
        foreach (var program in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var type = Load(Compile(program).GeneratedCode,
                symbols.Split(',', StringSplitOptions.RemoveEmptyEntries));
            Assert.Equal(expected, type.GetMethod("Probe")!.Invoke(null, null));
        }
    }

    [Theory]
    [InlineData("", 13)]
    [InlineData("FIRST", 11)]
    [InlineData("SECOND", 12)]
    [InlineData("FIRST,SECOND", 11)]
    public void StatementPreprocessor_AlternateDeclarationsRemainVisibleToTheirFollowingSibling(
        string symbols, int expected)
    {
        const string source = """
            module Conditions
              func Probe () → int
                pp FIRST
                  let int:value = 1
                pp elif SECOND
                  let int:value = 2
                pp else
                  let int:value = 3
                return value + 10
            """;
        foreach (var program in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var type = Load(Compile(program).GeneratedCode,
                symbols.Split(',', StringSplitOptions.RemoveEmptyEntries));
            Assert.Equal(expected, type.GetMethod("Probe")!.Invoke(null, null));
        }
    }

    [Theory]
    [InlineData("pp", "valid same-line")]
    [InlineData("pp FLAG &&", "valid same-line")]
    [InlineData("pp elif FLAG", "preceding branch")]
    [InlineData("pp else", "preceding branch")]
    [InlineData("pp FLAG\n  return 1\npp else FLAG", "cannot carry a condition")]
    [InlineData("pp FLAG\n  return 1\npp else\n  return 2\npp else", "preceding branch")]
    [InlineData("pp FLAG\n  return 1\npp else\n  return 2\npp elif OTHER", "preceding branch")]
    public void StatementPreprocessor_RejectsMalformedAndOrphanContinuations(string body, string message)
    {
        var source = "module Conditions\n  func Probe () → int\n" +
            string.Join("\n", body.Split('\n').Select(line => "    " + line)) + "\n      return 3";
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Message.Contains(message, StringComparison.Ordinal));
    }

    [Fact]
    public void StatementPreprocessor_ConverterRetainsExactInactiveStatementsInEveryBranch()
    {
        const string csharp = """
            public static class Conditions
            {
                public static int Probe()
                {
                    int n = 0;
            #if FIRST
                    n = 1;
            #elif SECOND
                    n = 2;
            #else
                    n = 3;
            #endif
                    return n;
                }
            }
            """;
        var conversion = new CSharpToCalorConverter().Convert(csharp);
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.Equal(2, conversion.Losses.Count);
        Assert.All(conversion.Losses, loss =>
        {
            Assert.Equal(ConversionLossKind.InteropPreserved, loss.Kind);
            Assert.Equal("preprocessor-disabled", loss.Feature);
        });
        Assert.Contains("pp FIRST", conversion.CalorSource);
        Assert.Contains("pp elif SECOND", conversion.CalorSource);
        foreach (var (symbols, expected) in new[] { ("", 3), ("FIRST", 1), ("SECOND", 2), ("FIRST,SECOND", 1) })
        {
            var type = Load(Compile(conversion.CalorSource!).GeneratedCode,
                symbols.Split(',', StringSplitOptions.RemoveEmptyEntries));
            Assert.Equal(expected, type.GetMethod("Probe")!.Invoke(null, null));
        }
    }

    [Fact]
    public void GotoLabel_PreservesAnEscapedNameAndJumpsOverTheBody()
        => AssertObservation("""
            var int:n = 0
            goto @return
            set n 100
            @return:
            set n n + 1
            return n
            """, "1");

    [Theory]
    [InlineData(">= 90", 95, "1")]
    [InlineData("> 90", 90, "0")]
    [InlineData("<= 90", 90, "1")]
    [InlineData("< 90", 95, "0")]
    public void RelationalMatchPattern_PreservesItsOperator(string pattern, int value, string expected)
        => AssertObservation($"return match {value}\n  case {pattern} → 1\n  case _ → 0", expected);

    [Theory]
    [InlineData(110, "110")]
    [InlineData(50, "0")]
    public void GuardedVarPattern_PreservesEscapedBindingAndGuard(int value, string expected)
        => AssertObservation($"return match {value}\n  case var @return when @return > 100 → @return\n  case _ → 0",
            expected);

    [Fact]
    public void GotoCaseAndDefault_PreserveSwitchControlFlow()
        => AssertObservation("""
            var int:n = 0
            match 1
              case 1
                set n n + 1
                goto case 2
              case 2
                set n n + 2
                goto default
              case _
                set n n + 4
            return n
            """, "7");

    [Fact]
    public void Label_CanBeTheLastStatementInAnOwnedBlock()
    {
        var source = """
            module Labels
              func Probe () → void
                done:
            """;
        foreach (var candidate in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var type = Load(Compile(candidate).GeneratedCode);
            Assert.Null(type.GetMethod("Probe")!.Invoke(null, null));
        }
    }

    [Theory]
    [InlineData(2, new[] { 0, 1 })]
    [InlineData(4, new[] { 0, 1, 2, 3, -1 })]
    public void YieldAndYieldBreak_ExerciseBothReachableIteratorBranches(int limit, int[] expected)
    {
        var source = """
            module Iterators
              func Probe (int:limit) → int
                for i from 0 to 3
                  if i == limit
                    yield break
                  yield i
                yield -1
            """;
        foreach (var candidate in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var type = Load(Compile(candidate).GeneratedCode);
            var sequence = Assert.IsAssignableFrom<IEnumerable<int>>(type.GetMethod("Probe")!.Invoke(null, [limit]));
            Assert.Equal(expected, sequence.ToArray());
        }
    }

    [Fact]
    public void Yield_DefersItsOperandAndExecutesFinallyOnEarlyDisposal()
    {
        var instrumentation = EmitInstrumentation("""
            public static string Trace = "";
            private static int Next() { Trace += "Y"; return 1; }
            private static void Release() { Trace += "F"; }
            """, "mut");
        var source = $"module Iterators\n{instrumentation}" + """
              func Probe () → int
                ! mut
                try
                  yield Next()
                  yield Next()
                finally
                  Release()
            """;
        foreach (var candidate in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var type = Load(Compile(candidate).GeneratedCode);
            var trace = type.Assembly.GetTypes().Single(item => item.Name == "Instrumentation").GetField("Trace")!;
            var sequence = Assert.IsAssignableFrom<IEnumerable<int>>(type.GetMethod("Probe")!.Invoke(null, null));
            Assert.Equal("", trace.GetValue(null));
            var enumerator = sequence.GetEnumerator();
            Assert.Equal("", trace.GetValue(null));
            Assert.True(enumerator.MoveNext());
            Assert.Equal(1, enumerator.Current);
            Assert.Equal("Y", trace.GetValue(null));
            enumerator.Dispose();
            Assert.Equal("YF", trace.GetValue(null));
        }
    }

    [Fact]
    public void ReturnedMatch_RequiresExplicitReturnAndRetainsExpressionArms()
        => AssertObservation("""
            return match 2
              case 0 → 10
              case 2 → 20
              case _ → 30
            """, "20");

    [Fact]
    public void ElseOnlyIf_FixPreservesCommentRawPayloadAndFollowingSibling()
    {
        var payload = "/* raw\r\n  untouched bytes\r\n*/";
        var source = "module Decisions\n  func Probe (int:n) → int\n    var int:result = 0\n" +
            "    if n == 0\n      set result 1\n    else // keep explanation\n" +
            "      // keep inner comment\n      if n == 1\n" +
            $"        raw statement {Encoding.UTF8.GetByteCount(payload)}\n{payload}\n" +
            "        set result 2\n    set result result + 10\n    return result\n";
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        var diagnostic = Assert.Single(diagnostics.DiagnosticsWithFixes,
            item => item.Message.Contains("else body containing only an if", StringComparison.Ordinal));
        var patch = new DiagnosticWithFix(diagnostic.Code, diagnostic.Message, diagnostic.Span,
            new SuggestedFix(diagnostic.Fix.Description, diagnostic.Fix.Edits, isSafeToApply: true));
        var fixedSource = DiagnosticFixes.Apply(source, source, "else-fix.calr", [patch], out var applied);
        Assert.Equal(diagnostic.Fix.Edits.Count, applied);
        Assert.Contains("// keep explanation", fixedSource);
        Assert.Contains("// keep inner comment", fixedSource);
        var tokens = new Lexer(fixedSource, new DiagnosticBag()).TokenizeAll();
        Assert.Equal(payload, Assert.Single(tokens.Where(token => token.Value is RawCSharpFrame))
            .Value is RawCSharpFrame frame ? frame.Code : null);
        _ = Parse(fixedSource);
        var type = Load(Compile(fixedSource).GeneratedCode);
        Assert.Equal(12, type.GetMethod("Probe")!.Invoke(null, [1]));
    }

    [Fact]
    public void NativeDo_PreservesOutParameterDefiniteAssignment()
    {
        var result = Compile("""
            module Migrated
              func Assign (out int:result) → void
                var int:i = 0
                do
                  set result i
                  set i i + 1
                while i < 2
            """);
        var type = Load(result.GeneratedCode);
        object?[] arguments = [null];
        type.GetMethod("Assign")!.Invoke(null, arguments);
        Assert.Equal(1, arguments[0]);
    }

    [Fact]
    public void EachDeconstruction_EvaluatesOnceAndDisposesOnContinueAndBreak()
        => AssertObservation("""
            var int:n = 0
            each k, v in Pairs()
              if k == 1
                continue
              set n n + v
              break
            return n
            """, "20:EABCD", """
            public static string Trace = "";
            private static System.Collections.Generic.IEnumerable<(int, int)> Pairs()
            {
                Trace += "E";
                return Enumerate();
            }
            private static System.Collections.Generic.IEnumerable<(int, int)> Enumerate()
            {
                Trace += "A";
                try
                {
                    Trace += "B"; yield return (1, 10);
                    Trace += "C"; yield return (2, 20);
                    Trace += "X"; yield return (3, 30);
                }
                finally { Trace += "D"; }
            }
            """);

    [Fact]
    public void EachDeconstruction_UsesCustomDeconstructAndDisposesOnNormalCompletion()
        => AssertObservation("""
            var int:n = 0
            each k, v in Pairs()
              if k == 1
                continue
              set n n + v
            return n
            """, "20:EABDCDF", """
            public static string Trace = "";
            private static System.Collections.Generic.IEnumerable<Pair> Pairs()
            {
                Trace += "E";
                return Enumerate();
            }
            private static System.Collections.Generic.IEnumerable<Pair> Enumerate()
            {
                Trace += "A";
                try
                {
                    Trace += "B"; yield return new Pair(1, 10);
                    Trace += "C"; yield return new Pair(2, 20);
                }
                finally { Trace += "F"; }
            }
            """, types: """
            public sealed class Pair
            {
                private readonly int key, value;
                public Pair(int key, int value) { this.key = key; this.value = value; }
                public void Deconstruct(out int key, out int value)
                {
                    Instrumentation.Trace += "D";
                    key = this.key;
                    value = this.value;
                }
            }
            """);

    [Fact]
    public void EachDeconstruction_DisposesWhenTheBodyThrows()
        => AssertObservation("""
            each k, v in Pairs()
              Fail()
            return 0
            """, "throws:InvalidOperationException:EABXF", """
            public static string Trace = "";
            private static void Fail()
            {
                Trace += "X";
                throw new System.InvalidOperationException();
            }
            private static System.Collections.Generic.IEnumerable<(int, int)> Pairs()
            {
                Trace += "E";
                return Enumerate();
            }
            private static System.Collections.Generic.IEnumerable<(int, int)> Enumerate()
            {
                Trace += "A";
                try { Trace += "B"; yield return (1, 10); }
                finally { Trace += "F"; }
            }
            """, effectCodes: "mut,throw");

    [Fact]
    public void CanonicalFor_RoundTripRetainsDistinctSemanticsAndHeaderSpans()
    {
        var source = """
            module Loops
              func Run (int:n) → void
                for i from 0 to n step 1
                  continue
                for j = 0; j < n; j = j + 1
                  break
            """;
        var module = Parse(source);
        var loops = module.Functions[0].Body.OfType<ForStatementNode>().ToArray();
        Assert.False(loops[0].ReevaluatesCondition);
        Assert.True(loops[1].ReevaluatesCondition);
        Assert.Equal("i", source.Substring(loops[0].VariableSpan.Start, loops[0].VariableSpan.Length));
        var emitted = new CalorEmitter().Emit(module);
        Assert.DoesNotContain('§', emitted);
        var reparsed = Parse(emitted).Functions[0].Body.OfType<ForStatementNode>().ToArray();
        Assert.False(reparsed[0].ReevaluatesCondition);
        Assert.True(reparsed[1].ReevaluatesCondition);
    }

    [Fact]
    public void NativeSet_EmitsAndRoundTripsWithoutLegacyStatementTags()
    {
        var source = """
            module Stores
              func Probe () → int
                var int:n = 0
                set n n + 3
                return n
            """;
        var emitted = new CalorEmitter().Emit(Parse(source));
        Assert.Contains("set n (n + 3)", emitted);
        Assert.DoesNotContain('§', emitted);
        var type = Load(Compile(emitted).GeneratedCode);
        Assert.Equal(3, type.GetMethod("Probe")!.Invoke(null, null));
    }

    [Theory]
    [InlineData("§L{i:0:3:1}")]
    [InlineData("§WH true")]
    [InlineData("§DO")]
    [InlineData("§EACH{item:int} xs")]
    [InlineData("§EACHKV{k:v} xs")]
    [InlineData("for {old:i:0:3:1}")]
    public void RemovedLoopForms_AreErrorsNotCompatibility(string header)
    {
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer($"module Bad\n  func Run (int[]:xs) → void\n    {header}\n      break",
            diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.True(diagnostics.HasErrors);
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        return module;
    }

    private static CompilationResult Compile(string source)
    {
        var result = Program.Compile(source, "canonical-controls.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            EnforceEffects = true,
            StatusWriter = TextWriter.Null
        });
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Errors) + "\n" + source);
        return result;
    }

    private static void AssertObservation(string body, string expected, string members = "",
        string overflow = "checked", string types = "", string? effectCodes = null, string declarations = "")
    {
        var raw = EmitInstrumentation(members, effectCodes ?? "mut") +
            (types.Length == 0 ? "" : $"  raw member {Encoding.UTF8.GetByteCount(types)}\n{types}\n");
        effectCodes ??= members.Length == 0 ? null : "mut";
        var effects = effectCodes == null ? "" : $"    ! {effectCodes}\n";
        var source = $"module Migrated overflow {overflow}\n{raw}{declarations}  func Probe () → int\n{effects}" +
            string.Join("\n", body.Split('\n').Select(line => "    " + line));
        AssertObservationSource(source, expected);
        var emitted = new CalorEmitter().Emit(Parse(source));
        AssertObservationSource(emitted, expected);
    }

    private static void AssertObservationSource(string source, string expected)
    {
        var result = Compile(source);
        var type = Load(result.GeneratedCode);
        string observed;
        try
        {
            observed = type.GetMethod("Probe")!.Invoke(null, null)!.ToString()!;
        }
        catch (TargetInvocationException exception)
        {
            observed = "throws:" + exception.InnerException!.GetType().Name;
        }
        var instrumentation = type.Assembly.GetTypes().SingleOrDefault(candidate => candidate.Name == "Instrumentation");
        var trace = instrumentation?.GetField("Trace")?.GetValue(null) as string;
        Assert.Equal(expected, observed + (string.IsNullOrEmpty(trace) ? "" : ":" + trace));
    }

    private static string EmitInstrumentation(string members, string effectCodes)
    {
        var source = CSharpSyntaxTree.ParseText("class Helpers {\n" + members + "\n}")
            .GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var text = new StringBuilder();
        var fields = source.Members.OfType<FieldDeclarationSyntax>().ToArray();
        var fieldNames = fields.SelectMany(field => field.Declaration.Variables)
            .Select(variable => variable.Identifier.Text).ToHashSet(StringComparer.Ordinal);
        if (fields.Length != 0)
        {
            var declaration = "public static class Instrumentation {\n" +
                string.Concat(fields.Select(field => field.ToFullString())) + "\n}";
            text.AppendLine($"  raw member {Encoding.UTF8.GetByteCount(declaration)}");
            text.Append(declaration).AppendLine();
        }
        foreach (var method in source.Members.OfType<MethodDeclarationSyntax>())
        {
            var parameters = string.Join(", ", method.ParameterList.Parameters
                .Select(parameter => $"{parameter.Type}:{parameter.Identifier.Text}"));
            text.AppendLine($"  func {method.Identifier.Text} private ({parameters}) → {method.ReturnType}");
            text.AppendLine($"    ! {effectCodes}");
            var body = method.Body!;
            var qualified = body.ReplaceNodes(body.DescendantNodes().OfType<IdentifierNameSyntax>()
                .Where(identifier => fieldNames.Contains(identifier.Identifier.Text)),
                (identifier, _) => SyntaxFactory.ParseExpression("Instrumentation." + identifier.Identifier.Text)
                    .WithTriviaFrom(identifier));
            var payload = string.Concat(qualified.Statements.Select(statement => statement.ToFullString()));
            text.AppendLine($"    raw statement {Encoding.UTF8.GetByteCount(payload)}");
            text.Append(payload).AppendLine();
        }
        return text.ToString();
    }

    private static Type Load(string code, IEnumerable<string>? preprocessorSymbols = null)
    {
        var compilation = CSharpCompilation.Create("CanonicalControls_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(code, CSharpParseOptions.Default
                .WithPreprocessorSymbols(preprocessorSymbols ?? []))], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics) + "\n" + code);
        return Assembly.Load(image.ToArray()).GetTypes()
            .Single(type => type.GetMethod("Probe") != null || type.GetMethod("Assign") != null);
    }
}
