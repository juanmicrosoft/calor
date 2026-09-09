using System.Reflection;
using System.Runtime.Loader;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Migration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public class TupleAssignmentMigrationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return Case("swap", "int a = 1; int b = 2; (a, b) = (b, a); return a * 10 + b;", "21", native: true);
        yield return Case("rotation", "int a = 1; int b = 2; int c = 3; (a, b, c) = (c, a, b); return a * 100 + b * 10 + c;", "312", native: true);
        yield return Case("repeated-target", "int a = 1; int b = 2; (a, a) = (b, a); return a;", "1", native: true);
        yield return Case("discard", "int a = 1; int b = 2; (_, a) = (a, b); return a;", "2", native: true);
        yield return Case("tuple-valued-rhs", "int a = 0; int b = 0; var pair = (2, 1); (a, b) = pair; return a * 10 + b;", "21", native: true);
        yield return Case("rhs-order", "int a = 0; int b = 0; (a, b) = (Next(), Next()); return log + \":\" + a + b;", "rr:12",
            "static int n; static string log = \"\"; static int Next() { log += \"r\"; n += 1; return n; }");
        yield return Case("properties", "(P, Q) = (Q, P); return log + \":\" + a + b;", "qpPQ:21",
            """
            static int a = 1; static int b = 2; static string log = "";
            public static int P { get { log += "p"; return a; } set { log += "P"; a = value; } }
            public static int Q { get { log += "q"; return b; } set { log += "Q"; b = value; } }
            """, native: true);
        yield return Case("target-order", "(Get().P, Get().P) = (Next(), Next()); return log + \":\" + box.P;", "ttrrww:2",
            """
            static string log = ""; static int n; static Box box = new Box();
            static Box Get() { log += "t"; return box; }
            static int Next() { log += "r"; n += 1; return n; }
            public class Box { int p; public int P { get { return p; } set { log += "w"; p = value; } } }
            """);
        yield return Case("index-order", "(Get()[Index()], Get()[Index()]) = (Next(), Next()); return log + \":\" + data[0] + data[1];", "titirr:12",
            """
            static string log = ""; static int i; static int n; static int[] data = new int[2];
            static int[] Get() { log += "t"; return data; }
            static int Index() { log += "i"; int result = i; i += 1; return result; }
            static int Next() { log += "r"; n += 1; return n; }
            """);
        yield return Case("overlapping-index", "int[] data = new int[] { 1, 2 }; (data[0], data[1]) = (data[1], data[0]); return data[0] * 10 + data[1];", "21");
        yield return Case("struct-storage", "Cell c = new Cell(); c.X = 1; c.Y = 2; (c.X, c.Y) = (c.Y, c.X); return c.X * 10 + c.Y;", "21",
            "public struct Cell { public int X; public int Y; }");
        yield return Case("nested-declaration", "var (a, (b, c)) = (1, (2, 3)); return a * 100 + b * 10 + c;", "123");
        yield return Case("custom-deconstruct", "var (a, b) = new Pair(); return a * 10 + b;", "21",
            "public class Pair { public void Deconstruct(out int a, out int b) { a = 2; b = 1; } }");
        yield return Case("mixed-declaration", "int a = 0; (a, int b) = (2, 1); return a * 10 + b;", "21");
        yield return Case("setter", "Set = 0; return a * 10 + b;", "21",
            "static int a = 1; static int b = 2; public static int Set { set => (a, b) = (b, a); }", native: true);
        yield return Case("expression-body", "Swap(); return a * 10 + b;", "21",
            "static int a = 1; static int b = 2; static void Swap() => (a, b) = (b, a);", native: true);
        yield return Case("assignment-value", "int a = 1; int b = 2; var result = ((a, b) = (b, a)); return result.Item1 * 100 + a * 10 + b;", "221");
        yield return Case("expression-body-value", "var result = Swap(); return result.Item1 * 100 + a * 10 + b;", "221",
            "static int a = 1; static int b = 2; static (int, int) Swap() => (a, b) = (b, a);");
        yield return Case("custom-indexer", "var box = new Box(); (box[0], box[1]) = (box[1], box[0]); return log + \":\" + box.Snapshot();", "ggss:21",
            """
            static string log = "";
            public record Box
            {
                int[] data = new int[] { 1, 2 };
                public int Snapshot() => data[0] * 10 + data[1];
                public int this[int index]
                {
                    get { log += "g"; return data[index]; }
                    set { log += "s"; data[index] = value; }
                }
            }
            """);
        yield return Case("native-conversions", "(P, Q) = (first, second); return log;", "ccPQ",
            """
            static string log = ""; static Value first; static Value second;
            public static int P { set { log += "P"; } }
            public static int Q { set { log += "Q"; } }
            public struct Value { public static implicit operator int(Value x) { log += "c"; return 0; } }
            """, native: true);
        yield return Case("conversion-order", "(P, Q) = (Make(1), Make(2)); return log;", "rcrcPQ",
            """
            static string log = "";
            public static int P { set { log += "P"; } }
            public static int Q { set { log += "Q"; } }
            static Value Make(int x) { log += "r"; return new Value(); }
            public struct Value { public static implicit operator int(Value x) { log += "c"; return 0; } }
            """);
        yield return Case("conversion-throws-before-write", "(P, Q) = (Make(1), Make(2)); return log;", "InvalidOperationException:rcrc",
            """
            static string log = ""; static int n;
            public static int P { set { log += "P"; } }
            public static int Q { set { log += "Q"; } }
            static Value Make(int x) { log += "r"; return new Value(); }
            public struct Value { public static implicit operator int(Value x) { log += "c"; n += 1; if (n == 2) throw new System.InvalidOperationException(); return 0; } }
            """);
    }

    private static object[] Case(string name, string body, string expected, string members = "", bool native = false)
        => [name, $$"""public class Subject { {{members}} public static object Probe() { {{body}} } }""", expected, native];

    [Theory]
    [MemberData(nameof(Cases))]
    public void DefaultConversion_PreservesRuntimeBehavior(string name, string source, string expected, bool native)
    {
        var original = Execute(source);
        Assert.Equal(expected, original);
        var conversion = new CSharpToCalorConverter().Convert(source);
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues.Select(i => i.Message)));
        Assert.DoesNotContain(conversion.Losses, loss => loss.Kind == ConversionLossKind.Dropped);
        if (native)
        {
            Assert.DoesNotContain(conversion.Losses, loss => loss.Feature == "tuple-deconstruction");
            Assert.Contains("§ASSIGN (", conversion.CalorSource);
        }
        else
        {
            Assert.Contains(conversion.Losses,
                loss => loss.Kind == ConversionLossKind.InteropPreserved && loss.Feature == "tuple-deconstruction");
        }
        var compiled = Program.Compile(conversion.CalorSource!, name + ".calr", new CompilationOptions
        {
            // Isolate evaluation semantics from unrelated nested-type effect inference.
            // The production generated-C# validation backstop remains enabled.
            EnforceEffects = native,
            StatusWriter = TextWriter.Null
        });
        Assert.False(compiled.HasErrors,
            string.Join("\n", compiled.Diagnostics.Errors) + "\n" + conversion.CalorSource);
        Assert.Equal(original, Execute(compiled.GeneratedCode));
    }

    private static string Execute(string source)
    {
        var compilation = CSharpCompilation.Create("TupleOracle_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble),
                CSharpSyntaxTree.ParseText(source)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics) + "\n" + source);
        image.Position = 0;
        var context = new AssemblyLoadContext("TupleOracle", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var subject = assembly.GetTypes().Single(type => type.Name == "Subject");
            try
            {
                return subject.GetMethod("Probe", BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, null)!.ToString()!;
            }
            catch (TargetInvocationException exception)
            {
                return exception.InnerException!.GetType().Name + ":" +
                    subject.GetField("log", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
            }
        }
        finally
        {
            context.Unload();
        }
    }
}
