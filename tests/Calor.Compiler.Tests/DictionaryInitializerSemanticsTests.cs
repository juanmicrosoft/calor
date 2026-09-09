using System.Reflection;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Migration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public class DictionaryInitializerSemanticsTests
{
    [Theory]
    [InlineData("var d = new Dictionary<int, int> { [1] = 2, [1] = 3 }; return d[1].ToString();", "3", true)]
    [InlineData("var d = new Dictionary<int, int> { {1, 2}, {1, 3} }; return d[1].ToString();", "throws:ArgumentException", false)]
    [InlineData("var d = new Dictionary<int, int> { {1, 2}, {2, 3} }; return d[2].ToString();", "3", false)]
    [InlineData("var d = new SortedDictionary<int, int> { {2, 20}, {1, 10} }; return d;", "SortedDictionary`2:1,2:10,20", true)]
    [InlineData("var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [\"A\"] = 1, [\"a\"] = 2 }; return d.Count + \":\" + d[\"A\"];", "1:2", true)]
    [InlineData("var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { {\"A\", 1}, {\"a\", 2} }; return d.Count.ToString();", "throws:ArgumentException", true)]
    [InlineData("var d = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [\"b\"] = 2, [\"A\"] = 1, [\"a\"] = 3 }; return string.Join(\",\", d.Keys) + \":\" + d[\"A\"];", "A,b:3", true)]
    [InlineData("var d = new ConcurrentDictionary<int, int> { [1] = 2, [1] = 3 }; return d;", "ConcurrentDictionary`2:1:3", true)]
    [InlineData("Dictionary<int, int> d = new() { [1] = 2, [1] = 3 }; return d[1].ToString();", "3", true)]
    [InlineData("Dictionary<string, int> d = new(StringComparer.OrdinalIgnoreCase) { {\"A\", 2} }; return d[\"a\"].ToString();", "2", true)]
    [InlineData("var d = new System.Collections.Generic.Dictionary<int, int> { [1] = 2, [1] = 3 }; return d[1].ToString();", "3", true)]
    [InlineData("var d = new D { {1, 2}, {1, 3} }; return d[1].ToString();", "throws:ArgumentException", true)]
    [InlineData("var d = (object)new Dictionary<int, int> { {1, 2}, {1, 3} }; return d;", "throws:ArgumentException", true)]
    [InlineData("var d = false ? new Dictionary<int, int> { {1, 2}, {1, 3} } : new Dictionary<int, int>(); return d.Count.ToString();", "0", true, "conditional-expression-hoisting")]
    [InlineData("var d = new Dictionary<int, int>(new Dictionary<int, int> { {1, 2}, {1, 3} }); return d;", "throws:ArgumentException", true)]
    [InlineData("Dictionary<int, int> d = null; d = new Dictionary<int, int> { {1, 2}, {1, 3} }; return d;", "throws:ArgumentException", false)]
    public void Migration_PreservesDictionaryObservations(
        string body, string expected, bool preserved, string preservedFeature = "dictionary-initializer")
    {
        AssertEquivalent(body, expected, preserved, preservedFeature: preservedFeature);
    }

    [Theory]
    [InlineData("var d = new Dictionary<int, int>(Capacity()) { [Key()] = Value(), [Key()] = Value() }; return Trace + \":\" + d[1];", "CKVKV:2")]
    [InlineData("var d = new Dictionary<int, int>(Capacity()) { {Key(), Value()}, {Key(), Value()}, {Key(), Value()} }; return Trace;", "throws:ArgumentException:CKVKV")]
    [InlineData("var d = new Dictionary<int, int> { {Key(), Value()}, {2, Value()} }; return Trace;", "KVV")]
    public void Migration_PreservesConstructorEntryOrderAndExceptionTiming(string body, string expected)
    {
        AssertEquivalent(body, expected, preserved: true);
    }

    [Theory]
    [InlineData("FrozenDictionary")]
    [InlineData("ImmutableDictionary")]
    [InlineData("ImmutableSortedDictionary")]
    public void UnconstructibleAdvertisedVariants_AreNotNormalizedToDictionary(string type)
    {
        var conversion = new CSharpToCalorConverter().Convert($$"""
            using System.Collections.Frozen;
            using System.Collections.Immutable;
            public class Migrated
            {
                public object Probe() => new {{type}}<int, int> { [1] = 2 };
            }
            """);
        Assert.DoesNotContain("§DICT", conversion.CalorSource);
        Assert.True(!conversion.Success || conversion.Losses.Any(loss =>
            loss.Kind == ConversionLossKind.InteropPreserved && loss.Feature == "dictionary-initializer"));
    }

    [Theory]
    [InlineData("private static Dictionary<int, int> D = new Dictionary<int, int> { [1] = 2 }; private static int Count = D.Count; public static object Probe() => Count.ToString();", "1")]
    [InlineData("private static Dictionary<int, int> D = new Dictionary<int, int> { {1, 2}, {1, 3} }; public static object Probe() => D;", "throws:TypeInitializationException")]
    [InlineData("private static int Before = Capacity(); private static Dictionary<int, int> D = new Dictionary<int, int> { [Key()] = Value() }; private static int After = Capacity(); public static object Probe() => Trace;", "CKVC")]
    [InlineData("private static int Before { get; } = Capacity(); private static Dictionary<int, int> D { get; } = new Dictionary<int, int> { [Key()] = Value() }; private static int After { get; } = Capacity(); public static object Probe() => Trace;", "CKVC")]
    [InlineData("private int Before = Capacity(); private Dictionary<int, int> D = new Dictionary<int, int> { [Key()] = Value() }; private int After = Capacity(); public static object Probe() { var value = new Migrated(); return Trace; }", "CKVC")]
    [InlineData("private static Dictionary<int, int> D { get; } = new Dictionary<int, int> { [1] = 2 }; private static int Count = D.Count; public static object Probe() => Count.ToString();", "1")]
    [InlineData("private static int Before { get; } = Capacity(); private static Dictionary<int, int> D = new Dictionary<int, int> { [Key()] = Value() }; private static int After { get; } = Capacity(); public static object Probe() => Trace;", "CKVC")]
    [InlineData("private int Before { get; } = Capacity(); private Dictionary<int, int> D = new Dictionary<int, int> { [Key()] = Value() }; private int After { get; } = Capacity(); public static object Probe() { var value = new Migrated(); return Trace; }", "CKVC")]
    public void Migration_PreservesTypeInitializerOrder(string members, string expected)
    {
        AssertEquivalent("", expected, preserved: true, members);
    }

    [Theory]
    [InlineData("object")]
    [InlineData("IDictionary<int, int>")]
    public void Migration_PreservesDeclaredLocalTypeAndOverloadResolution(string type)
    {
        AssertEquivalent("", "declared", preserved: true, $$"""
            private static string Pick({{type}} value) => "declared";
            private static string Pick(Dictionary<int, int> value) => "concrete";
            public static object Probe()
            {
                {{type}} d = new Dictionary<int, int> { {1, 2} };
                return Pick(d);
            }
            """);
    }

    private static void AssertEquivalent(
        string body, string expected, bool preserved, string? members = null,
        string preservedFeature = "dictionary-initializer")
    {
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using System.Collections.Concurrent;
            using D = System.Collections.Generic.Dictionary<int, int>;
            public class Migrated
            {
                private static string Trace = "";
                private static int Calls = 0;
                private static int Capacity() { Trace += "C"; return 2; }
                private static int Key() { Trace += "K"; return 1; }
                private static int Value() { Trace += "V"; Calls++; return Calls; }
                {{members ?? $"public static object Probe() {{ {body} }}"}}
            }
            """;
        var conversion = new CSharpToCalorConverter().Convert(source);
        Assert.True(conversion.Success, string.Join("; ", conversion.Issues.Select(issue => issue.Message)));
        Assert.DoesNotContain(conversion.Losses, loss => loss.Kind == ConversionLossKind.Dropped);
        if (preserved)
            Assert.Contains(conversion.Losses, loss =>
                loss.Kind == ConversionLossKind.InteropPreserved && loss.Feature == preservedFeature);
        else
            Assert.Contains("§DICT", conversion.CalorSource);
        var compiled = Program.Compile(conversion.CalorSource!, "dictionary.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            EnforceEffects = true,
            StatusWriter = TextWriter.Null
        });
        Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics.Errors));
        Assert.Equal(expected, Observe(source));
        Assert.Equal(expected, Observe(compiled.GeneratedCode));
    }

    private static string Observe(string source)
    {
        var compilation = CSharpCompilation.Create("DictionaryOracle_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        Assert.True(emit.Success, string.Join("; ", emit.Diagnostics) + "\n" + source);
        var assembly = Assembly.Load(image.ToArray());
        try
        {
            var value = assembly.GetTypes().Single(type => type.Name == "Migrated")
                .GetMethod("Probe")!.Invoke(null, null)!;
            if (value is IDictionary<int, int> dictionary)
                return value.GetType().Name + ":" + string.Join(",", dictionary.Keys)
                    + ":" + string.Join(",", dictionary.Values);
            return value.ToString()!;
        }
        catch (TargetInvocationException exception)
        {
            if (exception.InnerException is TypeInitializationException)
                return "throws:TypeInitializationException";
            var trace = assembly.GetTypes().Single(type => type.Name == "Migrated")
                .GetField("Trace", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
            return "throws:" + exception.InnerException!.GetType().Name
                + (trace is string { Length: > 0 } text ? ":" + text : "");
        }
    }
}
