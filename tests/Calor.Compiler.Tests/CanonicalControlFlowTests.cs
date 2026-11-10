using System.Reflection;
using System.Text;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Effects.Manifests;
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
            """, nativeMethods: """
              func Pairs private () → (int, int)
                yield (1, 10)

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
            """, declarations: NativeDisposable(), effectCodes: "mut,alloc", nativeMethods: """
              func Acquire private () → Resource
                ! mut,alloc
                set Instrumentation.Trace Instrumentation.Trace + "A"
                return new Resource()

            """);

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
        => """
              class Resource
                implements System.IDisposable
                func Dispose () → void
                  ! mut
                  set Instrumentation.Trace Instrumentation.Trace + "D"

            """;

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

    [Theory]
    [InlineData("object")]
    [InlineData("System.Object")]
    [InlineData("Root")]
    public void StaticObjectInitializer_UsesTheSelectedClrConstructorContract(string spelling)
    {
        var source = $$"""
            module Initializers
              using Root = System.Object
              class Holder
                field int:Value public static = 9
                field {{spelling}}:Gate public static = new {{spelling}}()
              func Probe () → int
                ! alloc
                return Holder.Value
            """;
        foreach (var candidate in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var parsed = Parse(candidate);
            var creation = Assert.IsType<NewExpressionNode>(parsed.Classes.Single().Fields[1].DefaultValue);
            var bindings = CallGraphAnalysis.ResolveOperatorCalls(parsed);
            var key = Assert.Single(bindings.StaticInitializerConstructors);
            Assert.Same(creation, key.Key);
            Assert.False(key.Value.FromStringFallback);
            Assert.Equal(EffectMemberKind.Constructor, key.Value.Kind);
            Assert.Equal("System.Object", key.Value.DeclaringType);
            Assert.Empty(key.Value.ParameterTypes!);
            var resolution = new EffectResolver(new ManifestLoader(loadUserLevelManifests: false))
                .Resolve(key.Value);
            Assert.Equal(EffectResolutionStatus.PureExplicit, resolution.Status);
            Assert.True(resolution.Effects.IsEmpty);

            var tree = CSharpSyntaxTree.ParseText(new CSharpEmitter().Emit(parsed));
            var projection = CSharpCompilation.Create("InitializerAuthority", [tree],
                GeneratedCSharpCompiler.References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.DoesNotContain(projection.GetDiagnostics(),
                diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
            var selected = Assert.IsAssignableFrom<IMethodSymbol>(projection.GetSemanticModel(tree).GetSymbolInfo(
                tree.GetRoot().DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Single()).Symbol);
            var clrConstructor = Assert.Single(projection.GetSpecialType(SpecialType.System_Object).InstanceConstructors,
                constructor => constructor.Parameters.Length == 0);
            Assert.True(SymbolEqualityComparer.Default.Equals(selected, clrConstructor));

            Assert.Equal(9, Load(Compile(candidate).GeneratedCode).GetMethod("Probe")!.Invoke(null, null));
            AssertMissingEffect(candidate.Replace("! alloc", "!", StringComparison.Ordinal), "Probe", "alloc");
        }
    }

    [Fact]
    public void StaticObjectInitializer_ChargesSiblingAutoPropertyAllocation()
    {
        const string source = """
            module Initializers
              class Holder
                field int:Value public static = 9
                property object:Gate static = new object()
                  get public
              func Probe () → int
                ! alloc
                return Holder.Value
            """;
        foreach (var candidate in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var parsed = Parse(candidate);
            Assert.Same(parsed.Classes.Single().Properties.Single().DefaultValue,
                Assert.Single(CallGraphAnalysis.ResolveOperatorCalls(parsed).StaticInitializerConstructors).Key);
            Assert.Equal(9, Load(Compile(candidate).GeneratedCode).GetMethod("Probe")!.Invoke(null, null));
            AssertMissingEffect(candidate.Replace("! alloc", "!", StringComparison.Ordinal), "Probe", "alloc");
        }
    }

    [Theory]
    [InlineData("field string:Other public static = System.Environment.CurrentDirectory")]
    [InlineData("property string:Other static = System.Environment.CurrentDirectory\n      get public")]
    [InlineData("field object:Other public static = Make()")]
    [InlineData("field System.Random:Other public static = new System.Random()")]
    public void StaticObjectInitializer_DoesNotHideUnprovedSiblingOperations(string other)
    {
        var source = $$"""
            module Initializers
              class Holder
                field int:Value public static = 9
                field object:Gate public static = new object()
                {{other}}
                func Make public static () → object
                  ! alloc
                  return new object()
              func Probe () → int
                ! alloc
                return Holder.Value
            """;
        foreach (var candidate in new[] { source, new CalorEmitter().Emit(Parse(source)) })
            AssertInitializationUnknown(candidate);
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("conditional")]
    [InlineData("opaque")]
    public void StaticObjectInitializer_UnprovedOwnersRemainInitializationBoundaries(string boundary)
    {
        const string payload = "public static object Extra = new object();";
        var member = boundary switch
        {
            "conditional" => "    pp HIDDEN\n      field int:Extra public static = 1\n",
            "opaque" => $"    raw member {Encoding.UTF8.GetByteCount(payload)}\n{payload}\n",
            _ => ""
        };
        var source = $"module Initializers\n  class Holder{(boundary == "partial" ? " partial" : "")}\n" +
            "    field int:Value public static = 9\n    field object:Gate public static = new object()\n" +
            member + "  func Probe () → int\n    ! alloc\n    return Holder.Value\n";
        foreach (var candidate in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var parsed = Parse(candidate);
            var diagnostics = new DiagnosticBag();
            new EffectEnforcementPass(diagnostics).Enforce(parsed);
            AssertUnknownInitialization(diagnostics);
            Assert.True(Program.Compile(candidate).HasErrors);
        }
    }

    [Theory]
    [InlineData("Foreign.Object")]
    [InlineData("Local")]
    public void StaticObjectInitializer_LookalikeAndAliasedSourceConstructorsAreNotClrAuthority(string spelling)
    {
        var source = $$"""
            module Initializers Foreign
              using Local = Foreign.Object
              class Object
                field int:Calls public static = 0
                ctor ()
                  set Calls Calls + 1
              class Holder
                field int:Value public static = 9
                field {{spelling}}:Gate public static = new {{spelling}}()
              func Probe () → int
                ! alloc,mut
                return Holder.Value
            """;
        foreach (var candidate in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var parsed = Parse(candidate);
            Assert.Empty(CallGraphAnalysis.ResolveOperatorCalls(parsed).StaticInitializerConstructors);
            AssertInitializationUnknown(candidate);
            var type = Load(new CSharpEmitter().Emit(parsed));
            Assert.Equal(9, type.GetMethod("Probe")!.Invoke(null, null));
            Assert.Equal(1, type.Assembly.GetType("Foreign.Object")!.GetField("Calls")!.GetValue(null));
        }
    }

    [Theory]
    [InlineData("field string:Broken public static = new object()")]
    [InlineData("field object:Broken public static = new object(1)")]
    public void StaticObjectInitializer_FailedProjectionProvidesNoConstructorAuthority(string broken)
    {
        var source = $$"""
            module Initializers
              class Holder
                field int:Value public static = 9
                field object:Gate public static = new object()
                {{broken}}
              func Probe () → int
                ! alloc
                return Holder.Value
            """;
        foreach (var candidate in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var parsed = Parse(candidate);
            Assert.Empty(CallGraphAnalysis.ResolveOperatorCalls(parsed).StaticInitializerConstructors);
            var diagnostics = new DiagnosticBag();
            new EffectEnforcementPass(diagnostics).Enforce(parsed);
            AssertUnknownInitialization(diagnostics);
            Assert.True(Program.Compile(candidate).HasErrors);
        }
    }

    [Fact]
    public void StaticObjectInitializer_ChargesStaticConstructorAndInitializesOnce()
    {
        const string source = """
            module Initializers
              class Holder
                field int:Calls public static = 0
                field object:Gate public static = new object()
                ctor stat ()
                  set Calls Calls + 1
              func Probe () → int
                ! alloc,mut
                var int:n = 0
                lock Holder.Gate
                  set n n + 1
                lock Holder.Gate
                  set n n + 1
                return n + Holder.Calls
            """;
        foreach (var candidate in new[] { source, new CalorEmitter().Emit(Parse(source)) })
            Assert.Equal(3, Load(Compile(candidate).GeneratedCode).GetMethod("Probe")!.Invoke(null, null));
        foreach (var (row, effect) in new[] { ("! alloc", "mut"), ("! mut", "alloc") })
        {
            var missing = source.Replace("! alloc,mut", row, StringComparison.Ordinal);
            foreach (var candidate in new[] { missing, new CalorEmitter().Emit(Parse(missing)) })
                AssertMissingEffect(candidate, "Probe", effect);
        }
    }

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
            Assert.Equal(new[]
            {
                "Changed += handler;", "Changed -= handler;",
                "Source().Changed += MakeHandler();", "Source().Changed -= MakeHandler();"
            }, new Lexer(program, new DiagnosticBag()).TokenizeAll()
                .Where(token => token.Value is RawCSharpFrame)
                .Select(token => ((RawCSharpFrame)token.Value!).Code));
            AssertUnmarkedInteropRejected(program, 4);
        }

        const string original = """
            public class Hub
            {
                public int Calls = 0;
                public event System.Action Changed
                {
                    add { Calls = Calls + 1; }
                    remove { Calls = Calls + 10; }
                }
                private void Handle() { return; }
                private Hub Source() { return this; }
                private System.Action MakeHandler() { return Handle; }
                public int Probe()
                {
                    System.Action handler = Handle;
                    Changed += handler;
                    Changed -= handler;
                    Source().Changed += MakeHandler();
                    Source().Changed -= MakeHandler();
                    return Calls;
                }
            }
            """;
        var originalType = Load(original);
        Assert.Equal(22, originalType.GetMethod("Probe")!.Invoke(Activator.CreateInstance(originalType), null));
        var conversion = new CSharpToCalorConverter().Convert(original);
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.NotEmpty(conversion.Losses);
        Assert.All(conversion.Losses, loss => Assert.Equal(ConversionLossKind.InteropPreserved, loss.Kind));
        foreach (var program in new[] { conversion.CalorSource,
            new CalorEmitter().Emit(Parse(conversion.CalorSource!)) })
        {
            var type = Load(Compile(program!).GeneratedCode);
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
    [InlineData("(and > 0 < 10)", 0, "0")]
    [InlineData("(and > 0 < 10)", 1, "1")]
    [InlineData("(and > 0 < 10)", 9, "1")]
    [InlineData("(and > 0 < 10)", 10, "0")]
    [InlineData("(and >= 10 < 100)", 9, "0")]
    [InlineData("(and >= 10 < 100)", 10, "1")]
    [InlineData("(and >= 10 < 100)", 99, "1")]
    [InlineData("(and >= 10 < 100)", 100, "0")]
    [InlineData("(or < -10 >= 10)", -11, "1")]
    [InlineData("(or < -10 >= 10)", -10, "0")]
    [InlineData("(or < -10 >= 10)", 9, "0")]
    [InlineData("(or < -10 >= 10)", 10, "1")]
    [InlineData("(and > (1 + 1) <= (3 * 3))", 2, "0")]
    [InlineData("(and > (1 + 1) <= (3 * 3))", 3, "1")]
    [InlineData("(and > (1 + 1) <= (3 * 3))", 9, "1")]
    [InlineData("(and > (1 + 1) <= (3 * 3))", 10, "0")]
    [InlineData("(and (or < -10 > 10) < 100)", -11, "1")]
    [InlineData("(and (or < -10 > 10) < 100)", 0, "0")]
    [InlineData("(and (or < -10 > 10) < 100)", 11, "1")]
    [InlineData("(and (or < -10 > 10) < 100)", 100, "0")]
    public void RelationalPatternCombinators_PreserveOperandBoundaries(
        string pattern, int value, string expected)
        => AssertObservation($"return match {value}\n  case {pattern} → 1\n  case _ → 0", expected);

    [Fact]
    public void Original094Patterns_ConvertAndReemitNativelyWithIndependentClrOracles()
    {
        var path = Path.Combine(CliTestHarness.FindRepoRoot(),
            "tests/TestData/CSharpImport/Level5_Advanced/094_PatternMatching.cs");
        var bytes = File.ReadAllBytes(path);
        var source = File.ReadAllText(path);
        var conversion = new CSharpToCalorConverter().Convert(source);
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.Empty(conversion.Losses);
        Assert.Equal(0, conversion.InteropPreservationCount);
        Assert.Equal(0, conversion.LossySubstitutionCount);
        Assert.Equal(0, conversion.DropCount);
        Assert.DoesNotContain("raw ", conversion.CalorSource!);
        Assert.DoesNotContain('§', conversion.CalorSource!);

        var native = Parse(conversion.CalorSource!);
        var methods = Assert.Single(native.Classes).Methods;
        Assert.Equal(new[] { "DescribeObject", "DescribeNumber", "GetLength" },
            methods.Select(method => method.Name));
        var numberMatch = Assert.IsType<MatchExpressionNode>(
            Assert.IsType<ReturnStatementNode>(methods[1].Body.Single()).Expression);
        Assert.Equal(5, numberMatch.Cases.Count);
        static (string Operator, long Value) Relational(PatternNode pattern)
        {
            var relation = Assert.IsType<RelationalPatternNode>(pattern);
            return (relation.Operator, Assert.IsType<IntLiteralNode>(relation.Value).Value);
        }
        Assert.Equal(("<", 0L), Relational(numberMatch.Cases[0].Pattern));
        Assert.Equal(0L, Assert.IsType<IntLiteralNode>(
            Assert.IsType<LiteralPatternNode>(numberMatch.Cases[1].Pattern).Literal).Value);
        var small = Assert.IsType<AndPatternNode>(numberMatch.Cases[2].Pattern);
        var medium = Assert.IsType<AndPatternNode>(numberMatch.Cases[3].Pattern);
        Assert.Equal((">", 0L), Relational(small.Left));
        Assert.Equal(("<", 10L), Relational(small.Right));
        Assert.Equal((">=", 10L), Relational(medium.Left));
        Assert.Equal(("<", 100L), Relational(medium.Right));
        Assert.IsType<WildcardPatternNode>(numberMatch.Cases[4].Pattern);

        var reemitted = new CalorEmitter().Emit(native);
        Assert.DoesNotContain("raw ", reemitted);
        Assert.DoesNotContain('§', reemitted);
        foreach (var code in new[] { source, Compile(conversion.CalorSource!).GeneratedCode,
            Compile(reemitted).GeneratedCode })
        {
            var owner = LoadAssembly(code).GetType("PatternMatching.Patterns")!;
            Assert.True(owner.IsAbstract && owner.IsSealed);
            var describeNumber = owner.GetMethod("DescribeNumber")!;
            Assert.Equal(typeof(string), describeNumber.ReturnType);
            Assert.Equal(typeof(int), Assert.Single(describeNumber.GetParameters()).ParameterType);
            foreach (var n in Enumerable.Range(-105, 215).Append(int.MinValue).Append(int.MaxValue))
            {
                var expected = n < 0 ? "negative" : n == 0 ? "zero" : n < 10 ? "small positive"
                    : n < 100 ? "medium positive" : "large positive";
                Assert.Equal(expected, describeNumber.Invoke(null, [n]));
            }
            var describeObject = owner.GetMethod("DescribeObject")!;
            Assert.Equal(typeof(string), describeObject.ReturnType);
            Assert.Equal(typeof(object), Assert.Single(describeObject.GetParameters()).ParameterType);
            foreach (var (input, expected) in new (object?, string)[]
            {
                (int.MinValue, "Integer: -2147483648"), (0, "Integer: 0"),
                (int.MaxValue, "Integer: 2147483647"), ("", "String: "), ("text", "String: text"),
                (false, "Boolean: False"), (true, "Boolean: True"), (null, "Unknown"),
                (7L, "Unknown"), (new object(), "Unknown")
            })
                Assert.Equal(expected, describeObject.Invoke(null, [input]));
            var getLength = owner.GetMethod("GetLength")!;
            Assert.Equal(typeof(int), getLength.ReturnType);
            Assert.Equal(typeof(object), Assert.Single(getLength.GetParameters()).ParameterType);
            foreach (var (input, expected) in new (object?, int)[]
            {
                ("", 0), ("text", 4), (Array.Empty<int>(), 0), (new[] { -1, 0, 7 }, 3),
                (null, 0), (7, -1), (false, -1), (new byte[2], -1), (new object(), -1)
            })
                Assert.Equal(expected, getLength.Invoke(null, [input]));
        }
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(110, "110")]
    [InlineData(50, "0")]
    public void GuardedVarPattern_PreservesEscapedBindingAndGuard(int value, string expected)
        => AssertObservation($"return match {value}\n  case var @return when @return > 100 → @return\n  case _ → 0",
            expected);

    [Theory]
    [InlineData("Calculator.Box<int> v when v.Active", "v")]
    [InlineData("Calculator.Box<int> { Active: true } v", "v")]
    [InlineData("Calculator.Box<int> { Active: true, } v", "v")]
    public void NativeCarrierPatterns_PreserveTypePropertyBindingAndRuntimeCases(string pattern, string binding)
    {
        var source = $$"""
            module PatternTests Calculator
              class Box<T>
                field bool:Active
                field T?:Value
              func Probe (object:value) → int
                return match value
                  case {{pattern}} → {{binding}}.Value
                  case _ → -1
            """;
        var parsed = Parse(source);
        var match = Assert.IsType<MatchExpressionNode>(
            Assert.IsType<ReturnStatementNode>(parsed.Functions.Single().Body.Single()).Expression);
        var typed = match.Cases[0].Pattern is AndPatternNode combined
            ? Assert.IsType<TypePatternNode>(combined.Left)
            : Assert.IsType<TypePatternNode>(match.Cases[0].Pattern);
        Assert.Equal("Calculator.Box<int>", typed.TypeName);
        Assert.Equal(binding.TrimStart('@'), typed.BindingName);
        Assert.Equal(typed.TypeName, source.Substring(typed.TypeNameSpan.Start, typed.TypeNameSpan.Length));
        Assert.Equal(binding, source.Substring(typed.BindingSpan!.Value.Start, typed.BindingSpan.Value.Length));
        var emitted = new CalorEmitter().Emit(parsed);
        Assert.DoesNotContain('§', emitted);
        foreach (var program in new[] { source, emitted })
        {
            var owner = Load(Compile(program).GeneratedCode);
            var probe = owner.GetMethod("Probe")!;
            var definition = owner.Assembly.GetType("Calculator.Box`1")!;
            var integers = definition.MakeGenericType(typeof(int));
            foreach (var active in new[] { true, false })
                foreach (var value in new[] { int.MinValue, -1, 0, 7, int.MaxValue })
                {
                    var input = Activator.CreateInstance(integers)!;
                    integers.GetField("Active")!.SetValue(input, active);
                    integers.GetField("Value")!.SetValue(input, value);
                    Assert.Equal(active ? value : -1, probe.Invoke(null, [input]));
                }
            var strings = definition.MakeGenericType(typeof(string));
            var wrongType = Activator.CreateInstance(strings)!;
            strings.GetField("Active")!.SetValue(wrongType, true);
            strings.GetField("Value")!.SetValue(wrongType, "not an integer");
            foreach (var input in new[] { wrongType, new object(), null })
                Assert.Equal(-1, probe.Invoke(null, [input]));
        }
    }

    [Fact]
    public void NativePropertyPatterns_RetainEveryMemberPredicate()
    {
        const string source = """
            module PropertyTests Calculator
              class Box<T>
                field bool:Active
                field T?:Value
              func Probe (Calculator.Box<int>:value) → int
                return match value
                  case { Active: true, Value: 7 } → 1
                  case _ → 0
            """;
        var parsed = Parse(source);
        var match = Assert.IsType<MatchExpressionNode>(
            Assert.IsType<ReturnStatementNode>(parsed.Functions.Single().Body.Single()).Expression);
        var property = Assert.IsType<PropertyPatternNode>(match.Cases[0].Pattern);
        Assert.Equal(new[] { "Active", "Value" }, property.Matches.Select(member => member.PropertyName));
        foreach (var program in new[] { source, new CalorEmitter().Emit(parsed) })
        {
            var owner = Load(Compile(program).GeneratedCode);
            var carrier = owner.Assembly.GetType("Calculator.Box`1")!.MakeGenericType(typeof(int));
            foreach (var active in new[] { true, false })
                foreach (var value in new[] { 0, 7, 8 })
                {
                    var input = Activator.CreateInstance(carrier)!;
                    carrier.GetField("Active")!.SetValue(input, active);
                    carrier.GetField("Value")!.SetValue(input, value);
                    Assert.Equal(active && value == 7 ? 1 : 0, owner.GetMethod("Probe")!.Invoke(null, [input]));
                }
        }
    }

    [Theory]
    [InlineData("Calculator.Option<int>", "{ IsSome: true, Value: var v }", false)]
    [InlineData("object", "Calculator.Option<int> { IsSome: true, Value: var v }", false)]
    [InlineData("object", "Calculator.Option<int> { IsSome: true, Value: var v } carrier", false)]
    [InlineData("Calculator.Option<int>", "{ IsSome: true, Value: var v }", true)]
    public void NativePropertyPayloadCapture_UsesDeclaredGenericMemberType(
        string inputType, string pattern, bool properties)
    {
        var source = $$"""
            module Captures Calculator
              class Option<T>
                field bool:IsSome public
                field T?:Value public
              func Probe ({{inputType}}:value) → int
                return match value
                  case {{pattern}} → v
                  case _ → -1
            """;
        if (properties)
            source = source.Replace("field bool:IsSome public", "property bool:IsSome public\n      get\n      set")
                .Replace("field T?:Value public", "property T?:Value public\n      get\n      set");
        void Set(Type type, object instance, string name, object value)
        {
            if (properties)
                type.GetProperty(name)!.SetValue(instance, value);
            else
                type.GetField(name)!.SetValue(instance, value);
        }
        foreach (var program in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var diagnostics = new DiagnosticBag();
            var bound = new Binding.Binder(diagnostics).Bind(Parse(program));
            Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
            var capture = Assert.Single(bound.SymbolsById.Values.OfType<VariableSymbol>()
                .Where(symbol => symbol.Name == "v"));
            Assert.Equal("INT", capture.TypeName);
            Assert.False(capture.HasUnknownInferredType);
            var owner = Load(Compile(program).GeneratedCode);
            var integers = owner.Assembly.GetType("Calculator.Option`1")!.MakeGenericType(typeof(int));
            foreach (var present in new[] { true, false })
                foreach (var payload in new[] { int.MinValue, -1, 0, 7, int.MaxValue })
                {
                    var input = Activator.CreateInstance(integers)!;
                    Set(integers, input, "IsSome", present);
                    Set(integers, input, "Value", payload);
                    Assert.Equal(present ? payload : -1, owner.GetMethod("Probe")!.Invoke(null, [input]));
                }
            Assert.Equal(-1, owner.GetMethod("Probe")!.Invoke(null, [null]));
            if (inputType == "object")
            {
                var strings = integers.GetGenericTypeDefinition().MakeGenericType(typeof(string));
                var wrong = Activator.CreateInstance(strings)!;
                Set(strings, wrong, "IsSome", true);
                Set(strings, wrong, "Value", "not an integer");
                Assert.Equal(-1, owner.GetMethod("Probe")!.Invoke(null, [wrong]));
                Assert.Equal(-1, owner.GetMethod("Probe")!.Invoke(null, [new object()]));
            }
        }
    }

    [Fact]
    public void NativeNestedPropertyCapture_PreservesConstructedMemberTypesAndNullChecks()
    {
        const string source = """
            module Captures Calculator
              class Option<T>
                field bool:IsSome public
                field T?:Value public
              class Envelope<T>
                field T?:Inner public
              func Probe (object:value) → int
                return match value
                  case Calculator.Envelope<Calculator.Option<int>> { Inner: { IsSome: true, Value: var v } } → v
                  case _ → -1
            """;
        foreach (var program in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var diagnostics = new DiagnosticBag();
            var bound = new Binding.Binder(diagnostics).Bind(Parse(program));
            Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
            Assert.Equal("INT", Assert.Single(bound.SymbolsById.Values.OfType<VariableSymbol>()
                .Where(symbol => symbol.Name == "v")).TypeName);
            var owner = Load(Compile(program).GeneratedCode);
            var option = owner.Assembly.GetType("Calculator.Option`1")!.MakeGenericType(typeof(int));
            var envelope = owner.Assembly.GetType("Calculator.Envelope`1")!.MakeGenericType(option);
            var outer = Activator.CreateInstance(envelope)!;
            var inner = Activator.CreateInstance(option)!;
            envelope.GetField("Inner")!.SetValue(outer, inner);
            foreach (var present in new[] { true, false })
                foreach (var payload in new[] { int.MinValue, 0, 7, int.MaxValue })
                {
                    option.GetField("IsSome")!.SetValue(inner, present);
                    option.GetField("Value")!.SetValue(inner, payload);
                    Assert.Equal(present ? payload : -1, owner.GetMethod("Probe")!.Invoke(null, [outer]));
                }
            envelope.GetField("Inner")!.SetValue(outer, null);
            Assert.Equal(-1, owner.GetMethod("Probe")!.Invoke(null, [outer]));
            foreach (var input in new[] { inner, new object(), null })
                Assert.Equal(-1, owner.GetMethod("Probe")!.Invoke(null, [input]));
        }
    }

    [Theory]
    [InlineData("string", "string?", "STRING?", "payload")]
    [InlineData("int?", "int?", "INT?", 7)]
    public void NativeNullablePropertyCapture_PreservesActualClosedPayloadType(
        string argument, string result, string capturedType, object payload)
    {
        var source = $$"""
            module Captures Calculator
              class Option<T>
                field T?:Value public
              func Probe (Calculator.Option<{{argument}}>:value) → {{result}}
                return match value
                  case { Value: var v } → v
                  case _ → null
            """;
        foreach (var program in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var diagnostics = new DiagnosticBag();
            var bound = new Binding.Binder(diagnostics).Bind(Parse(program));
            Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
            Assert.Equal(capturedType, Assert.Single(bound.SymbolsById.Values.OfType<VariableSymbol>()
                .Where(symbol => symbol.Name == "v")).TypeName);
            var owner = Load(Compile(program).GeneratedCode);
            var closed = owner.Assembly.GetType("Calculator.Option`1")!
                .MakeGenericType(argument == "string" ? typeof(string) : typeof(int?));
            var input = Activator.CreateInstance(closed)!;
            foreach (var value in new[] { payload, null })
            {
                closed.GetField("Value")!.SetValue(input, value);
                Assert.Equal(value, owner.GetMethod("Probe")!.Invoke(null, [input]));
            }
            Assert.Null(owner.GetMethod("Probe")!.Invoke(null, [null]));
        }
    }

    [Fact]
    public void NativeTypedPropertyPayloadCapture_RejectsWrongRuntimePayloadTypesAndNulls()
    {
        const string source = """
            module Captures Calculator
              class Option<T>
                field bool:IsSome public
                field T?:Value public
              func Probe (object:value) → int
                return match value
                  case Calculator.Option<object> { IsSome: true, Value: int v } → v
                  case _ → -1
            """;
        foreach (var program in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var owner = Load(Compile(program).GeneratedCode);
            var closed = owner.Assembly.GetType("Calculator.Option`1")!.MakeGenericType(typeof(object));
            var input = Activator.CreateInstance(closed)!;
            foreach (var present in new[] { true, false })
                foreach (var payload in new object?[] { int.MinValue, 0, int.MaxValue, "wrong", new object(), null })
                {
                    closed.GetField("IsSome")!.SetValue(input, present);
                    closed.GetField("Value")!.SetValue(input, payload);
                    Assert.Equal(present && payload is int value ? value : -1,
                        owner.GetMethod("Probe")!.Invoke(null, [input]));
                }
            Assert.Equal(-1, owner.GetMethod("Probe")!.Invoke(null, [null]));
        }
    }

    [Theory]
    [InlineData("Value: var v", "string", "int", "v")]
    [InlineData("Missing: var v", "int", "int", "v")]
    [InlineData("Value: true", "int", "int", "1")]
    public void NativePropertyCapture_RejectsWrongPayloadTypesAndUnknownMembers(
        string memberPattern, string argument, string result, string body)
    {
        var source = $$"""
            module Captures Calculator
              class Option<T>
                field T?:Value public
              func Probe (Calculator.Option<{{argument}}>:value) → {{result}}
                return match value
                  case { {{memberPattern}} } → {{body}}
                  case _ → 0
            """;
        var compiled = Program.Compile(source, "bad-capture.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            StatusWriter = TextWriter.Null
        });
        Assert.True(compiled.HasErrors);
        Assert.Contains(compiled.Diagnostics.Errors, error =>
            error.Code is DiagnosticCode.TypeMismatch or DiagnosticCode.UndefinedReference);
    }

    [Fact]
    public void NativePropertyCapture_DoesNotAliasAnUnrelatedQualifiedType()
    {
        const string source = """
            module Captures Calculator
              class Option<T>
                field T?:Value public
              func Probe (object:value) → int
                return match value
                  case Other.Option<int> { Value: var v } → v
                  case _ → 0
            """;
        var compiled = Program.Compile(source, "bad-qualified-capture.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            StatusWriter = TextWriter.Null
        });
        Assert.True(compiled.HasErrors);
        Assert.Contains(compiled.Diagnostics.Errors, error => error.Message.Contains(
            "Cannot resolve native property-pattern member 'Value' on type 'Other.Option", StringComparison.Ordinal));
    }

    [Fact]
    public void NullableGenericReference_RequiresAnActualNativeClassDeclaration()
        => Assert.Throws<ArgumentException>(() => new TypeChecking.NullableReferenceType(
            new TypeChecking.GenericInstanceType("Unresolved", [TypeChecking.PrimitiveType.Int])));

    [Theory]
    [InlineData("int", 1)]
    [InlineData("string", 0)]
    [InlineData("System.Int32 _", 1)]
    [InlineData("System.String _", 0)]
    [InlineData("int[]", 0)]
    public void NativeTypeOnlyPatterns_DoNotBecomeCatchAllBindings(string pattern, int expected)
        => AssertObservation($"let object:value = 7\nreturn match value\n  case {pattern} → 1\n  case _ → 0",
            expected.ToString());

    [Fact]
    public void NativeQualifiedGenericTypeDiscard_RoundTripsAsATypeTest()
    {
        const string source = """
            module PatternTests Calculator
              class Box<T>
                field T?:Value
              func Probe (object:value) → int
                return match value
                  case Calculator.Box<int> _ → 1
                  case _ → 0
            """;
        foreach (var program in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var owner = Load(Compile(program).GeneratedCode);
            var definition = owner.Assembly.GetType("Calculator.Box`1")!;
            var probe = owner.GetMethod("Probe")!;
            Assert.Equal(1, probe.Invoke(null, [Activator.CreateInstance(definition.MakeGenericType(typeof(int)))]));
            Assert.Equal(0, probe.Invoke(null, [Activator.CreateInstance(definition.MakeGenericType(typeof(string)))]));
            Assert.Equal(0, probe.Invoke(null, [null]));
        }
    }

    [Fact]
    public void NativeTypePattern_PreservesEscapedScalarBinding()
        => AssertObservation("""
            let object:value = 7
            return match value
              case int @return when @return > 0 → @return
              case _ → -1
            """, "7");

    [Theory]
    [InlineData("{ Active true }")]
    [InlineData("{ Active: }")]
    [InlineData("{ Active: true")]
    [InlineData("Calculator.Box<i32> v")]
    [InlineData("{ Value: §VAR{v} }")]
    [InlineData("§PPROP §PMATCH{Value} §VAR{v}")]
    public void NativeCarrierPatterns_RejectMalformedPropertiesAndRetiredTypeAliases(string pattern)
    {
        var source = $"module Bad\n  func Probe (object:value) → int\n    return match value\n" +
            $"      case {pattern} → 1\n      case _ → 0";
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.True(diagnostics.HasErrors);
    }

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
        var (instrumentation, _, _) = EmitInstrumentation("""
            public static string Trace = "";
            private static int Next() { Trace += "Y"; return 1; }
            private static void Release() { Trace += "F"; }
            """);
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
        var rawToken = Assert.Single(tokens.Where(token => token.Value is RawCSharpFrame));
        Assert.Equal(payload, Assert.IsType<RawCSharpFrame>(rawToken.Value).Code);
        _ = Parse(fixedSource);
        AssertUnmarkedInteropRejected(fixedSource, 1);

        // The raw fixture is a byte-transport oracle, not admitted executable C#.
        var comment = CSharpSyntaxTree.ParseText(payload);
        Assert.Empty(comment.GetCompilationUnitRoot().Members);
        Assert.DoesNotContain(comment.GetDiagnostics(), item => item.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var nativeSource = fixedSource.Remove(rawToken.Span.Start, rawToken.Span.Length);
        var type = Load(Compile(nativeSource).GeneratedCode);
        Assert.Equal(12, type.GetMethod("Probe")!.Invoke(null, [1]));
    }

    [Fact]
    public void NativeDo_PreservesOutParameterDefiniteAssignment()
    {
        var result = Compile("""
            module Migrated
              func Assign (out int:result) → void
                ! mut
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
    public void NativeDo_OutParameterStillRequiresMutation()
    {
        const string source = """
            module Migrated
              func Assign (out int:result) → void
                var int:i = 0
                do
                  set result i
                  set i i + 1
                while i < 2
            """;
        var parsed = Parse(source);
        foreach (var candidate in new[] { source, new CalorEmitter().Emit(parsed) })
        {
            _ = Parse(candidate);
            var result = Program.Compile(candidate, "out-mutation.calr", new CompilationOptions
            {
                EnableTypeChecking = true,
                EnforceEffects = true,
                StatusWriter = TextWriter.Null
            });
            Assert.True(result.HasErrors);
            Assert.Contains(result.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
                && error.Message.Contains("'Assign'", StringComparison.Ordinal)
                && error.Message.Contains("'mut'", StringComparison.Ordinal));
            var type = Load(new CSharpEmitter().Emit(Parse(candidate)));
            object?[] arguments = [null];
            type.GetMethod("Assign")!.Invoke(null, arguments);
            Assert.Equal(1, arguments[0]);
        }
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
            """, nativeMethods: """
              func Pairs private () → System.Collections.Generic.IEnumerable<(int, int)>
                ! mut
                set Instrumentation.Trace Instrumentation.Trace + "E"
                return Enumerate()
              func Enumerate private () → (int, int)
                ! mut
                set Instrumentation.Trace Instrumentation.Trace + "A"
                try
                  set Instrumentation.Trace Instrumentation.Trace + "B"
                  yield (1, 10)
                  set Instrumentation.Trace Instrumentation.Trace + "C"
                  yield (2, 20)
                  set Instrumentation.Trace Instrumentation.Trace + "X"
                  yield (3, 30)
                finally
                  set Instrumentation.Trace Instrumentation.Trace + "D"

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
            """, declarations: """
              class Pair sealed
                field int:key private readonly
                field int:value private readonly
                ctor (int:key, int:value)
                  set this.key key
                  set this.value value
                func Deconstruct (out int:key, out int:value) → void
                  ! mut
                  set Instrumentation.Trace Instrumentation.Trace + "D"
                  set key this.key
                  set value this.value

            """, nativeMethods: """
              func Pairs private () → System.Collections.Generic.IEnumerable<Pair>
                ! mut,alloc
                set Instrumentation.Trace Instrumentation.Trace + "E"
                return Enumerate()
              func Enumerate private () → Pair
                ! mut,alloc
                set Instrumentation.Trace Instrumentation.Trace + "A"
                try
                  set Instrumentation.Trace Instrumentation.Trace + "B"
                  yield new Pair(1, 10)
                  set Instrumentation.Trace Instrumentation.Trace + "C"
                  yield new Pair(2, 20)
                finally
                  set Instrumentation.Trace Instrumentation.Trace + "F"

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
            """, effectCodes: "mut,throw", nativeMethods: """
              func Fail private () → void
                ! mut,throw,alloc
                set Instrumentation.Trace Instrumentation.Trace + "X"
                throw new System.InvalidOperationException()
              func Pairs private () → System.Collections.Generic.IEnumerable<(int, int)>
                ! mut
                set Instrumentation.Trace Instrumentation.Trace + "E"
                return Enumerate()
              func Enumerate private () → (int, int)
                ! mut
                set Instrumentation.Trace Instrumentation.Trace + "A"
                try
                  set Instrumentation.Trace Instrumentation.Trace + "B"
                  yield (1, 10)
                finally
                  set Instrumentation.Trace Instrumentation.Trace + "F"

            """);

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
        _ = Parse(source);
        var result = Program.Compile(source, "canonical-controls.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            EnforceEffects = true,
            StatusWriter = TextWriter.Null
        });
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Errors) + "\n" + source);
        return result;
    }

    private static void AssertMissingEffect(string source, string caller, string effect)
    {
        _ = Parse(source);
        var result = Program.Compile(source, "missing-initializer-effect.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            EnforceEffects = true,
            StatusWriter = TextWriter.Null
        });
        Assert.True(result.HasErrors);
        Assert.Equal($"Function '{caller}' uses effect '{effect}' but does not declare it",
            Assert.Single(result.Diagnostics.Errors).Message);
        Assert.Equal(DiagnosticCode.ForbiddenEffect, result.Diagnostics.Errors.Single().Code);
        Assert.DoesNotContain(result.Diagnostics, error => error.Code == DiagnosticCode.UnknownExternalCall
            || error.Message.Contains("'unknown'", StringComparison.Ordinal));
    }

    private static void AssertInitializationUnknown(string source)
    {
        _ = Parse(source);
        var result = Program.Compile(source, "unproved-initializer.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            EnforceEffects = true,
            StatusWriter = TextWriter.Null
        });
        Assert.True(result.HasErrors);
        AssertUnknownInitialization(result.Diagnostics);
    }

    private static void AssertUnknownInitialization(DiagnosticBag diagnostics)
    {
        Assert.Contains(diagnostics, error => error.Code == DiagnosticCode.UnknownExternalCall
            && error.Message.Contains("Holder..cctor", StringComparison.Ordinal));
        Assert.Contains(diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
            && error.Message.Contains("'Probe'", StringComparison.Ordinal)
            && error.Message.Contains("'unknown'", StringComparison.Ordinal));
    }

    private static void AssertUnmarkedInteropRejected(string source, int frames)
    {
        var result = Program.Compile(source, "canonical-controls.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            EnforceEffects = true,
            StatusWriter = TextWriter.Null
        });
        Assert.True(result.HasErrors);
        Assert.Equal(frames, result.Diagnostics.Errors.Count());
        Assert.All(result.Diagnostics.Errors,
            diagnostic => Assert.Equal(DiagnosticCode.InvalidCSharpInteropReason, diagnostic.Code));
    }

    private static void AssertObservation(string body, string expected, string members = "",
        string overflow = "checked", string types = "", string? effectCodes = null, string declarations = "",
        string? nativeMethods = null)
    {
        var (support, helperEffects, originalMethods) = EmitInstrumentation(
            members, nativeMethods == null ? types : "", nativeMethods);
        effectCodes ??= members.Length == 0 ? null : "mut";
        // Native instrumentation exposes effects that its old raw bodies hid.
        var requiredEffects = (effectCodes?.Split(',') ?? []).Concat(helperEffects).Distinct().ToArray();
        var effects = requiredEffects.Length == 0 ? "" : $"    ! {string.Join(",", requiredEffects)}\n";
        var source = $"module Migrated overflow {overflow}\n{support}{declarations}  func Probe () → int\n{effects}" +
            string.Join("\n", body.Split('\n').Select(line => "    " + line));
        AssertObservationSource(source, expected, originalMethods, types);
        var emitted = new CalorEmitter().Emit(Parse(source));
        AssertObservationSource(emitted, expected, originalMethods, types);
    }

    private static void AssertObservationSource(string source, string expected,
        MethodDeclarationSyntax[] originalMethods, string originalTypes)
    {
        var result = Compile(source);
        AssertGeneratedObservation(result.GeneratedCode, expected);
        if (originalMethods.Length == 0)
            return;
        var originalByName = originalMethods.ToDictionary(method => method.Identifier.ValueText);
        var generated = CSharpSyntaxTree.ParseText(result.GeneratedCode).GetRoot();
        var helpers = generated.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(method => originalByName.ContainsKey(method.Identifier.ValueText)).ToArray();
        Assert.Equal(originalMethods.Length, helpers.Length);
        // Hold the generated control body fixed while restoring the original C# helpers.
        var original = generated.ReplaceNodes(helpers,
            (method, _) => originalByName[method.Identifier.ValueText]);
        var types = CSharpSyntaxTree.ParseText(originalTypes).GetRoot()
            .DescendantNodes().OfType<ClassDeclarationSyntax>().ToDictionary(type => type.Identifier.ValueText);
        var supportingTypes = original.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Where(type => types.ContainsKey(type.Identifier.ValueText)).ToArray();
        Assert.Equal(types.Count, supportingTypes.Length);
        original = original.ReplaceNodes(supportingTypes, (type, _) => types[type.Identifier.ValueText]);
        AssertGeneratedObservation(original.ToFullString(), expected);
    }

    private static void AssertGeneratedObservation(string code, string expected)
    {
        var type = Load(code);
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

    private static (string Source, string[] Effects, MethodDeclarationSyntax[] OriginalMethods) EmitInstrumentation(
        string members, string types = "", string? nativeMethods = null)
    {
        if (members.Length == 0 && types.Length == 0)
            return ("", [], []);
        var source = CSharpSyntaxTree.ParseText("class Helpers {\n" + members + "\n}")
            .GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        Assert.All(source.Members.OfType<MethodDeclarationSyntax>(), method =>
        {
            Assert.Empty(method.AttributeLists);
            Assert.Null(method.TypeParameterList);
            Assert.DoesNotContain(method.Modifiers, modifier => modifier.IsKind(SyntaxKind.AsyncKeyword));
        });
        var fields = source.Members.OfType<FieldDeclarationSyntax>().ToArray();
        var fieldNames = fields.SelectMany(field => field.Declaration.Variables)
            .Select(variable => variable.Identifier.Text).ToHashSet(StringComparer.Ordinal);
        var methods = new List<MethodDeclarationSyntax>();
        foreach (var method in source.Members.OfType<MethodDeclarationSyntax>())
        {
            var body = method.Body!;
            var qualified = body.ReplaceNodes(body.DescendantNodes().OfType<IdentifierNameSyntax>()
                .Where(identifier => fieldNames.Contains(identifier.Identifier.Text)),
                (identifier, _) => SyntaxFactory.ParseExpression("Instrumentation." + identifier.Identifier.Text)
                    .WithTriviaFrom(identifier));
            methods.Add(method.WithBody(qualified).NormalizeWhitespace());
        }
        var original = "public static class Instrumentation {\n" +
            string.Concat(fields.Select(field => field.ToFullString())) + "\n}\n" +
            (nativeMethods == null ? "public static class Helpers {\n" +
                string.Join("\n", methods.Select(method => method.ToFullString())) + "\n}\n" : "") + types;
        var conversion = new CSharpToCalorConverter().Convert(original);
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.Empty(conversion.Losses);
        var converted = Assert.IsType<ModuleNode>(conversion.Ast);
        var helpers = nativeMethods == null
            ? Assert.Single(converted.Classes, type => type.Name == "Helpers") : null;
        if (helpers != null)
            Assert.Empty(helpers.InteropBlocks);
        var functions = helpers == null ? Parse("module Supporting\n" + nativeMethods).Functions.ToArray()
            : helpers.Methods.Select(method => new FunctionNode(
                method.Span, method.Id, method.Name, method.Visibility, method.Parameters,
                method.Output, method.Effects, method.Body, method.Attributes)).ToArray();
        Assert.Equal(methods.Select(method => method.Identifier.ValueText).Order(),
            functions.Select(function => function.Name).Order());
        var supporting = converted.With(update =>
        {
            update.Classes = converted.Classes.Where(type => type != helpers).ToArray();
            update.Functions = functions;
            update.Items = converted.Items.Count == 0 ? []
                : converted.Items.Where(item => item != helpers).Concat(functions).ToArray();
        });
        var emitted = new CalorEmitter().Emit(supporting);
        Assert.StartsWith("module ", emitted);
        Assert.DoesNotContain(new Lexer(emitted, new DiagnosticBag()).TokenizeAll(),
            token => token.Value is RawCSharpFrame);
        return (emitted[(emitted.IndexOf('\n') + 1)..], functions
            .SelectMany(function => function.Effects?.Effects
                .SelectMany(effect => effect.Value.Split(',')
                    .Select(value => EffectCodes.ToCompact(effect.Key, value.Trim())))
                ?? Enumerable.Empty<string>())
            .Distinct().ToArray(), methods.ToArray());
    }

    private static Type Load(string code, IEnumerable<string>? preprocessorSymbols = null)
        => LoadAssembly(code, preprocessorSymbols).GetTypes()
            .Single(type => type.GetMethod("Probe") != null || type.GetMethod("Assign") != null);

    private static Assembly LoadAssembly(string code, IEnumerable<string>? preprocessorSymbols = null)
    {
        var compilation = CSharpCompilation.Create("CanonicalControls_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(code, CSharpParseOptions.Default
                .WithPreprocessorSymbols(preprocessorSymbols ?? []))], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics) + "\n" + code);
        return Assembly.Load(image.ToArray());
    }
}
