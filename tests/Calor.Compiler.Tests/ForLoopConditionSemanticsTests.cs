using System.Reflection;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Migration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public class ForLoopConditionSemanticsTests
{
    [Theory]
    [InlineData("int limit = 3; int count = 0; for (int i = 0; i < limit; i++) { count++; limit--; } return count;", "2", false)]
    [InlineData("int count = 0; for (int i = 0; i < Bound(); i++) count++; return count;", "3:BBBB", false)]
    [InlineData("int count = 0; int j = 0; for (int i = 0; i < 3; j++) { count++; i++; } return count + j;", "6", false)]
    [InlineData("int count = 0; for (int i = 1; i < 16; i *= 2) count++; return count;", "4", false)]
    [InlineData("int count = 0; for (int i = 100; i > 1; i /= 2) count++; return count;", "6", false)]
    [InlineData("int count = 0; for (int i = 1; i < 16; i <<= 1) count++; return count;", "4", false)]
    [InlineData("int count = 0; for (int i = 16; i > 0; i >>= 1) count++; return count;", "5", false)]
    [InlineData("int count = 0; for (int i = 10; i > 0; i -= 2) count++; return count;", "5", false)]
    [InlineData("int count = 0; for (int i = 0; i < Bound(); i++) { if (i == 1) continue; count++; } return count;", "2:BBBB", false)]
    [InlineData("int count = 0; for (int i = 0; i < Bound(); i++) { try { if (i == 1) continue; count++; } finally { Trace += \"F\"; } } return count;", "2:BFBFBFB", false)]
    [InlineData("int count = 0; for (int i = 0; i < Bound(); i++) { for (int j = 0; j < 2; j++) { if (j == 0) continue; count++; } if (i == 1) continue; count++; } return count;", "5:BBBB", false)]
    [InlineData("int count = 0; int i = 0; for (; i < 3; i++) { if (i == 1) continue; count++; } return count + i;", "5", false)]
    [InlineData("int count = 0; for (int i = 0, j = 3; i < j; i++, j--) count++; return count;", "2", false)]
    [InlineData("int count = 0; for (int i = 0; ; i++) { count++; if (i == 2) break; } return count;", "3", false)]
    [InlineData("int count = 0; for (int i = 0; i < 3; ) { i++; count++; } return count;", "3", false)]
    [InlineData("int count = 0; for (int i = 0; i != 3; i++) count++; return count;", "3", false)]
    [InlineData("int count = 0; for (int i = 0; i <= int.MaxValue; i++) { count++; if (count == 2) break; i = int.MaxValue; } return count;", "2", false)]
    [InlineData("int count = 0; for (int i = 0; i < 5; i++) { i++; count++; } return count;", "3", false)]
    [InlineData("int count = 0; for (long i = 0; i < 3; i++) count++; return count;", "3", false)]
    [InlineData("int count = 0; for (int i = 0; i < 3; i++) count++; return count;", "3", true)]
    [InlineData("int count = 0; for (int i = 0; i <= 3; ++i) { if (i == 1) continue; count++; } return count;", "3", true)]
    [InlineData("int count = 0; for (int i = 3; i > 0; i--) count++; return count;", "3", true)]
    [InlineData("int count = 0; for (int i = 3; i >= 0; --i) count++; return count;", "4", true)]
    [InlineData("int count = 0; for (int i = 3; i < 0; i++) count++; return count;", "0", true)]
    [InlineData("int count = 0; for (int i = 0; i < Bound(); i += Step()) { if (i == 1) continue; count++; } return count;", "2:BSBSBSB", false)]
    [InlineData("int count = 0; for (int i = 0; i < ExplodingBound(); i++) count++; return count;", "throws:InvalidOperationException:BBB", false)]
    [InlineData("int count = 0; for (int i = int.MaxValue; i <= int.MaxValue; i++) { count++; if (count == 2) break; } return count;", "2", false)]
    [InlineData("int count = 0; for (int i = 0; i < Bound(); Tick()) { i++; count++; } return count;", "3:BTBTBTB", false)]
    [InlineData("int count = 0; for (int i = 0; i < Bound(); ThrowingStep()) count++; return count;", "throws:InvalidOperationException:BS", false)]
    [InlineData("int count = 0; int step = 1; for (int i = 0; i < 10; i += step) { count++; step++; } return count;", "4", false)]
    [InlineData("int count = 0; int limit = 2; for (int i = 0; i < limit; i++) count++; for (int i = 0; i < limit; i++) count++; return count;", "4", false)]
    [InlineData("int count = 0; for (int i = 0; i < Bound(); i += Step()) { checked { using (new Scope()) { if (i == 1) continue; count++; } } } return count;", "2:BCDSBCDSBCDSB", false)]
    [InlineData("int count = 0; for (int i = 0; i < 0 && Bound() > 0; i++) count++; return count;", "0", false)]
    [InlineData("int count = 0; for (int i = 0; i < 1 || Bound() > 0; i++) { count++; break; } return count;", "1", false)]
    [InlineData("int count = 0; for (int i = 0; i < 0 ? Bound() > 0 : false; i++) count++; return count;", "0", false)]
    [InlineData("int count = 0; for (byte i = 0; i < 3; i++) count++; return count;", "3", false)]
    [InlineData("int count = 0; for (short i = 0; i < 3; i += 1) count++; return count;", "3", false)]
    [InlineData("int count = 0; for (int i = 0; i == i++;) { count++; if (count == 3) break; } return count;", "3", false)]
    [InlineData("int count = 0; for (int i = (count = Step()); i < 0; i++) { } return count;", "1:S", false)]
    [InlineData("int count = 0; for (int i = 0; i < 2; i.ToString(), i.ToString()) { i++; count++; } return count;", "2", false)]
    [InlineData("int count = 0; for (int i = 0; i < Bound(); i++) { int Bound = 1; Bound++; count += Bound; } return count;", "6:BBBB", false)]
    public void Migration_PreservesLoopObservations(string body, string expected, bool native)
        => AssertEquivalent(body, expected, native);

    [Theory]
    [InlineData("Change(in i);")]
    [InlineData("Change(i);")]
    [InlineData("ref readonly int alias = ref i; Change(in alias);")]
    [InlineData("System.TypedReference alias = __makeref(i); __refvalue(alias, int) = int.MaxValue;")]
    public void ReadonlyReferenceEscape_DoesNotClaimNativeOverflowEquivalence(string mutation)
    {
        AssertEquivalent($$"""
            int count = 0;
            for (int i = 0; i < 3; i++)
            {
                count++;
                if (count == 2) break;
                unchecked { {{mutation}} }
            }
            return count;
            """, "2", native: false, """
            private static unsafe void Change(in int value)
            {
                unchecked
                {
                    fixed (int* pointer = &value) { *pointer = int.MaxValue; }
                }
            }
            """);
    }

    [Fact]
    public void PatternVariableHeader_PreservesBindingAfterSuccessfulMatch()
        => AssertEquivalent("int n = 0; for (object x = 1; x is int i; x = null) n += i; return n;",
            "1", native: false, preserved: true);

    [Fact]
    public void InitializerOutVariable_PreservesHeaderStorage()
        => AssertEquivalent(
            "int n = 0; for (int i = Init(out int end); i < end; i++) n++; return n;",
            "2", native: false, preserved: true, members:
            "private static int Init(out int end) { end = 2; return 0; }");

    [Fact]
    public void HeaderCall_DoesNotMoveBeforeEarlierStateRead()
        => AssertEquivalent(
            "int n = 0; State = 1; for (; State < Reset(); ) { n++; break; } return n;",
            "0", native: false, members: """
            private static int State;
            private static int Reset() { State = 0; return 1; }
            """);

    [Fact]
    public void BodyLocal_DoesNotShadowFieldUsedByCondition()
        => AssertEquivalent(
            "int n = 0; for (int i = 0; i < limit; i++) { int limit = 1; limit++; n += limit; } return n;",
            "6", native: false, members: "private static int limit = 3;");

    [Fact]
    public void MultipleAwaitedIncrementors_ExecuteAsStatementsInOrder()
        => AssertEquivalent(
            "int i = 0; for (; i < 2; await AsyncTick(), await AsyncTick()) i++; return i;",
            "2:TTTT", native: false, asyncProbe: true, members: """
            private static System.Threading.Tasks.Task AsyncTick()
            {
                Trace += "T";
                return System.Threading.Tasks.Task.CompletedTask;
            }
            """);

    [Fact]
    public void OverloadedIncrementHeader_DoesNotSubstituteAddition()
        => AssertEquivalent(
            "int n = 0; for (Counter c = new Counter(); c.N < 3; c++) n++; return n;",
            "3", native: false, types: """
            public struct Counter
            {
                public int N;
                public static Counter operator ++(Counter x) => new Counter { N = x.N + 1 };
                public static Counter operator +(Counter x, int y) => new Counter { N = x.N + 2 };
            }
            """);

    [Theory]
    [InlineData("")]
    [InlineData("public static bool operator !(Truth x) => true;")]
    public void UserDefinedCondition_UsesTrueOperatorNotNegation(string negation)
        => AssertEquivalent(
            "int n = 0; for (Truth t = default; t;) { n++; break; } return n;",
            "1", native: false, members: $$"""
            public struct Truth
            {
                public static bool operator true(Truth x) => true;
                public static bool operator false(Truth x) => false;
                {{negation}}
            }
            """);

    private static void AssertEquivalent(string body, string expected, bool native, string members = "",
        bool preserved = false, string types = "", bool asyncProbe = false)
    {
        var returnType = asyncProbe ? "async System.Threading.Tasks.Task<int>" : "int";
        var source = $$"""
            public static class Migrated
            {
                public static string Trace = "";
                {{members}}
                private static int Bound() { Trace += "B"; return 3; }
                private static int Step() { Trace += "S"; return 1; }
                private static void Tick() { Trace += "T"; }
                private static void ThrowingStep() { Trace += "S"; throw new System.InvalidOperationException(); }
                private sealed class Scope : System.IDisposable
                {
                    public Scope() { Trace += "C"; }
                    public void Dispose() { Trace += "D"; }
                }
                private static int ExplodingBound()
                {
                    Trace += "B";
                    if (Trace.Length == 3) throw new System.InvalidOperationException();
                    return 3;
                }
                public static {{returnType}} Probe() { {{body}} }
            }
            {{types}}
            """;
        var conversion = new CSharpToCalorConverter().Convert(source);
        Assert.True(conversion.Success, string.Join("; ", conversion.Issues.Select(issue => issue.Message)));
        Assert.DoesNotContain(conversion.Losses, loss => loss.Kind == ConversionLossKind.Dropped);
        if (preserved)
        {
            Assert.Contains(conversion.Losses, loss =>
                loss.Kind == ConversionLossKind.InteropPreserved && loss.Feature == "for");
            Assert.Contains("for (", conversion.CalorSource);
        }
        else if (native)
            Assert.Contains("§L{", conversion.CalorSource);
        else
            Assert.True(conversion.CalorSource!.Contains("§WH{"),
                conversion.CalorSource + "\n" + string.Join("; ", conversion.Issues.Select(issue => issue.Message)));
        var compiled = Program.Compile(conversion.CalorSource!, "for.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            EnforceEffects = true,
            StatusWriter = TextWriter.Null
        });
        Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics.Errors) + "\n" + conversion.CalorSource);
        Assert.Equal(expected, Observe(source));
        Assert.Equal(expected, Observe(compiled.GeneratedCode));
    }

    private static string Observe(string source)
    {
        var compilation = CSharpCompilation.Create("ForOracle_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        Assert.True(emit.Success, string.Join("; ", emit.Diagnostics) + "\n" + source);
        var type = Assembly.Load(image.ToArray()).GetTypes().Single(type => type.Name == "Migrated");
        string? result;
        try
        {
            var value = type.GetMethod("Probe")!.Invoke(null, null)!;
            if (value is Task task)
            {
                task.GetAwaiter().GetResult();
                value = task.GetType().GetProperty("Result")!.GetValue(task)!;
            }
            result = value.ToString();
        }
        catch (TargetInvocationException exception)
        {
            result = "throws:" + exception.InnerException!.GetType().Name;
        }
        var trace = (string)type.GetField("Trace")!.GetValue(null)!;
        return result + (trace.Length > 0 ? ":" + trace : "");
    }
}
