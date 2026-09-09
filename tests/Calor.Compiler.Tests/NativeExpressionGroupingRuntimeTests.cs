using System.Reflection;
using Calor.Compiler.CodeGen;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public class NativeExpressionGroupingRuntimeTests
{
    [Theory]
    [InlineData("(upper (+ a b))", "str", "a", "b", "AB")]
    [InlineData("(lower (+ a b))", "str", "A", "B", "ab")]
    [InlineData("(len (+ a b))", "i32", "a", "b", 2)]
    [InlineData("(char-at (+ a b) INT:1)", "char", "a", "b", 'b')]
    public void InstanceReceivers_UseCompleteChildExpression(
        string expression, string returnType, string a, string b, object expected)
    {
        var type = Compile($$"""
            §M{m1:Grouping}
              §F{f1:Probe:pub} (str:a, str:b) -> {{returnType}}
                §E{}
                §R {{expression}}
            """);
        Assert.Equal(expected, Invoke(type, "Probe", a, b));
    }

    [Theory]
    [InlineData("(+ (?? x INT:1) INT:2)", 7, 3)]
    [InlineData("(* INT:2 (?? x INT:1))", 10, 2)]
    [InlineData("(- INT:20 (+ (?? x INT:1) INT:2))", 13, 17)]
    [InlineData("(cast i32 (+ (cast f64 (?? x INT:1)) FLOAT:0.75))", 5, 1)]
    public void CoalescingArithmeticAndCasts_PreserveComposition(string expression, int present, int absent)
    {
        var type = Compile($$"""
            §M{m1:Grouping}
              §F{f1:Probe:pub} (?i32:x) -> i32
                §E{}
                §R {{expression}}
            """);
        Assert.Equal(present, Invoke(type, "Probe", 5));
        Assert.Equal(absent, Invoke(type, "Probe", new object?[] { null }));
    }

    [Fact]
    public void CastAndUnaryComposition_PreserveCompleteOperands()
    {
        var type = Compile("""
            §M{m1:Grouping}
              §F{f1:CastSum:pub} (f64:x, f64:y) -> i32
                §E{}
                §R (cast i32 (+ x y))
              §F{f2:NegatedSum:pub} (f64:x, f64:y) -> i32
                §E{}
                §R (- (cast i32 (+ x y)))
              §F{f3:Character:pub} (i32:x, i32:y) -> char
                §E{}
                §R (char-from-code (+ x y))
              §F{f4:Code:pub} (str:a, str:b) -> i32
                §E{}
                §R (char-code (char-at (+ a b) INT:1))
            """);
        foreach (var (x, y) in new[] { (1.75, 2.75), (-1.75, -2.75), (1.25, -2.75) })
        {
            Assert.Equal((int)(x + y), Invoke(type, "CastSum", x, y));
            Assert.Equal(-(int)(x + y), Invoke(type, "NegatedSum", x, y));
        }
        Assert.Equal('A', Invoke(type, "Character", 60, 5));
        Assert.Equal((int)'b', Invoke(type, "Code", "a", "b"));
    }

    [Fact]
    public void NestedPatterns_MatchBooleanOracleAcrossBoundedDomain()
    {
        var cases = new (string Pattern, Func<int, bool> Oracle)[]
        {
            ("(not (and §PREL{gte} 0 §PREL{lte} 10))", x => !(x >= 0 && x <= 10)),
            ("(not (or §PREL{lt} 0 §PREL{gt} 10))", x => !(x < 0 || x > 10)),
            ("(and (or §PREL{lt} 0 §PREL{gt} 10) §PREL{lt} 20)", x => (x < 0 || x > 10) && x < 20),
            ("(or §PREL{lt} 0 (and §PREL{gte} 10 (not §PREL{gt} 20)))", x => x < 0 || x >= 10 && !(x > 20)),
            ("(not (not (or §PREL{lt} 0 §PREL{gt} 10)))", x => !!(x < 0 || x > 10))
        };
        foreach (var (pattern, oracle) in cases)
        {
            var type = Compile($$"""
                §M{m1:Grouping}
                  §F{f1:Probe:pub} (i32:x) -> bool
                    §E{}
                    §R §W{w1} x
                      §K {{pattern}} → BOOL:true
                      §K _ → BOOL:false
                """);
            foreach (var input in Enumerable.Range(-12, 45))
                Assert.Equal(oracle(input), Invoke(type, "Probe", input));
        }
    }

    [Fact]
    public void GroupedReceiversAndCoalescing_EvaluateChildrenOnceInOrder()
    {
        var type = Compile("""
            §M{m1:Grouping}
              §F{f1:RecordText:priv} (List<str>:trace, str:value) -> str
                §E{mut}
                §C{trace.Add} §A value §/C
                §R value
              §F{f2:RecordNumber:priv} (List<i32>:trace, i32:value) -> i32
                §E{mut}
                §C{trace.Add} §A value §/C
                §R value
              §F{f3:Text:pub} (List<str>:trace) -> str
                §E{mut}
                §R (upper (+ §C{RecordText} §A trace §A "a" §/C §C{RecordText} §A trace §A "b" §/C))
              §F{f4:Number:pub} (?i32:value, List<i32>:trace) -> i32
                §E{mut}
                §R (+ (?? value §C{RecordNumber} §A trace §A INT:1 §/C) §C{RecordNumber} §A trace §A INT:2 §/C)
            """);
        var textTrace = new List<string>();
        Assert.Equal("AB", Invoke(type, "Text", textTrace));
        Assert.Equal(["a", "b"], textTrace);
        var numberTrace = new List<int>();
        Assert.Equal(7, Invoke(type, "Number", 5, numberTrace));
        Assert.Equal([2], numberTrace);
        numberTrace.Clear();
        Assert.Equal(3, Invoke(type, "Number", null, numberTrace));
        Assert.Equal([1, 2], numberTrace);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{false}")]
    public async Task AwaitCoalescedTask_AwaitsSelectedReceiver(string configuration)
    {
        var type = Compile($$"""
            §M{m1:Grouping}
              §AF{f1:Probe:pub} (Task<i32>:first, Task<i32>:second) -> i32
                §R §AWAIT{{configuration}} (?? first second)
            """);
        foreach (var first in new[] { Task.FromResult(5), null })
        {
            var result = Assert.IsAssignableFrom<Task<int>>(Invoke(type, "Probe", first, Task.FromResult(9)));
            Assert.Equal(first == null ? 9 : 5, await result);
        }
    }

    private static object? Invoke(Type type, string method, params object?[] arguments) =>
        type.GetMethod(method)!.Invoke(null, arguments);

    private static Type Compile(string source)
    {
        var result = Program.Compile(source, "grouping.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            EnforceEffects = true,
            StatusWriter = TextWriter.Null
        });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var compilation = CSharpCompilation.Create("Grouping_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + result.GeneratedCode)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics));
        return Assembly.Load(stream.ToArray()).GetType("Grouping.GroupingModule", throwOnError: true)!;
    }
}
