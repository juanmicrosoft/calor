using System.Reflection;
using System.Runtime.Loader;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class CanonicalOperatorIndexerTests
{
    private const string Money = """
        module Operators
          class Money
            property long:Cents
              get
            ctor (long:cents)
              set Cents cents
            op + (Money:a, Money:b) → Money
              return new Money(a.Cents + b.Cents)
            op implicit (long:cents) → Money
              return new Money(cents)
        """;

    private const string Bag = """
        module Indexers
          class Bag
            field int[]:_items private = new int[3]
            this[int:i] → int
              get
                return _items[i]
              set
                set _items[i] value
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CountedOperatorsExecuteOnTheClr(bool roundtrip)
    {
        var source = roundtrip ? new CalorEmitter().Emit(Parse(Money)) : Money;
        Assert.DoesNotContain('§', source);
        Assert.Contains("op + (Money:a, Money:b) → Money", source);
        var result = Program.Compile(source);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.Equal(42, Execute(result.GeneratedCode, AddMoney));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CountedIndexerLoadsAndStoresExecuteOnTheClr(bool roundtrip)
    {
        var source = roundtrip ? new CalorEmitter().Emit(Parse(Bag)) : Bag;
        Assert.DoesNotContain('§', source);
        Assert.Contains("this[int:i] → int", source);
        var result = Program.Compile(source);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.Equal(42, Execute(result.GeneratedCode, ReadAndWriteIndexer));
    }

    [Theory]
    [InlineData("public class Bag { private int[] _items = new int[3]; public int this[int i] { get { return _items[i]; } set { _items[i] = value; } } }")]
    [InlineData("public class Bag { private int[] _items = new int[3]; internal int this[int @step] { get { return _items[@step]; } set { _items[@step] = value; } } }")]
    [InlineData("public class Bag { private int[] _items = new int[3]; public int this[int i] { get { return _items[i]; } private set { _items[i] = value; } } }")]
    public void RealConverterPreservesIndexerVisibilityAndClrBehavior(string source)
    {
        var converted = new CSharpToCalorConverter().Convert(source);
        Assert.True(converted.Success, string.Join("\n", converted.Context.Issues));
        Assert.False(converted.CalorSource!.Contains("raw ", StringComparison.Ordinal),
            string.Join("\n", converted.Context.Issues.Select(issue => issue.Message)) + "\n" + converted.CalorSource);
        Assert.DoesNotContain('§', converted.CalorSource!);
        var original = Execute(source, ReadAndWriteIndexer);
        var result = Program.Compile(converted.CalorSource!);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.Equal(original, Execute(result.GeneratedCode, ReadAndWriteIndexer));
        var expected = source.Contains("internal int", StringComparison.Ordinal)
            ? Visibility.Internal : Visibility.Public;
        Assert.Equal(expected, Parse(converted.CalorSource!).Classes.Single().Indexers.Single().Visibility);
        if (source.Contains("private set", StringComparison.Ordinal))
            Assert.Equal(Visibility.Private, Parse(converted.CalorSource!).Classes.Single().Indexers.Single().Setter!.Visibility);
    }

    [Fact]
    public void ThisExpressionStatementsAreNotMistakenForIndexerDeclarations()
    {
        var source = Bag + """

                func Touch () → void
                  return
                func Exercise () → int
                  this.Touch()
                  return this[1]
            """;
        foreach (var input in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var result = Program.Compile(input);
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.Equal(0, Execute(result.GeneratedCode, assembly =>
            {
                var type = assembly.GetType("Bag", throwOnError: true)!;
                return (int)type.GetMethod("Exercise")!.Invoke(Activator.CreateInstance(type), null)!;
            }));
        }
    }

    [Fact]
    public void RealConverterRetainsOperatorsWithoutRawRescue()
    {
        const string source = """
            public class Money
            {
                public long Cents { get; }
                public Money(long cents) { Cents = cents; }
                public static Money operator +(Money a, Money b) { return new Money(a.Cents + b.Cents); }
                public static implicit operator Money(long cents) { return new Money(cents); }
            }
            """;
        var converted = new CSharpToCalorConverter().Convert(source);
        Assert.True(converted.Success, string.Join("\n", converted.Context.Issues));
        Assert.DoesNotContain("raw ", converted.CalorSource);
        Assert.DoesNotContain('§', converted.CalorSource!);
        var result = Program.Compile(converted.CalorSource!);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.Equal(Execute(source, AddMoney), Execute(result.GeneratedCode, AddMoney));
    }

    [Fact]
    public void InterfaceIndexerPreservesBracketParametersAndAccessors()
    {
        var module = Parse("""
            module Indexers
              interface IBag
                this[int:@step, string:key] → int
                  get
                  set
            """);
        var emitted = new CalorEmitter().Emit(module);
        var indexer = Parse(emitted).Interfaces.Single().Indexers.Single();
        Assert.Equal(new[] { "step", "key" }, indexer.Parameters.Select(parameter => parameter.Name));
        Assert.NotNull(indexer.Getter);
        Assert.NotNull(indexer.Setter);
        var result = Program.Compile(emitted);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Execute(result.GeneratedCode, _ => 0);
    }

    [Theory]
    [InlineData("this(int:i) → int")]
    [InlineData("this[int:i]")]
    [InlineData("this[] → int")]
    [InlineData("this[int:i] → int")]
    [InlineData("op + (C:a, C:b)")]
    public void MalformedRetainedHeadersAreDiagnosed(string header)
    {
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer($"module Bad\n  class C\n    {header}", diagnostics)
            .TokenizeAllForParser(), diagnostics).Parse();
        Assert.True(diagnostics.HasErrors);
    }

    private static long ReadAndWriteIndexer(Assembly assembly)
    {
        var type = assembly.GetType("Bag", throwOnError: true)!;
        var instance = Activator.CreateInstance(type);
        var indexer = type.GetProperty("Item", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;
        indexer.SetValue(instance, 42, [1]);
        return (int)indexer.GetValue(instance, [1])!;
    }

    private static long AddMoney(Assembly assembly)
    {
        var type = assembly.GetType("Money", throwOnError: true)!;
        var a = Activator.CreateInstance(type, 17L);
        var b = type.GetMethod("op_Implicit")!.Invoke(null, [25L]);
        var sum = type.GetMethod("op_Addition")!.Invoke(null, [a, b]);
        return (long)type.GetProperty("Cents")!.GetValue(sum)!;
    }

    private static long Execute(string source, Func<Assembly, long> observe)
    {
        var compilation = CSharpCompilation.Create("RetainedCapabilities",
            [CSharpSyntaxTree.ParseText(source)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext("RetainedCapabilities", isCollectible: true);
        try { return observe(context.LoadFromStream(stream)); }
        finally { context.Unload(); }
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return module;
    }
}
