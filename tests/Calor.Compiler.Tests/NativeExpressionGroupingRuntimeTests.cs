using System.Reflection;
using Calor.Compiler.CodeGen;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public class NativeExpressionGroupingRuntimeTests
{
    [Theory]
    [InlineData("(a + b).ToUpper()", "string", "a", "b", "AB")]
    [InlineData("(a + b).ToLower()", "string", "A", "B", "ab")]
    [InlineData("(a + b).Length", "int", "a", "b", 2)]
    [InlineData("(a + b)[1]", "char", "a", "b", 'b')]
    public void InstanceReceivers_UseCompleteChildExpression(
        string expression, string returnType, string a, string b, object expected)
    {
        var type = Compile($$"""
            module Grouping Grouping
              module GroupingModule
                func Probe (string:a, string:b) → {{returnType}}
                  !
                  return {{expression}}
            """);
        Assert.Equal(expected, Invoke(type, "Probe", a, b));
    }

    [Theory]
    [InlineData("(x ?? 1) + 2", 7, 3)]
    [InlineData("2 * (x ?? 1)", 10, 2)]
    [InlineData("20 - ((x ?? 1) + 2)", 13, 17)]
    [InlineData("(int)((double)(x ?? 1) + 0.75)", 5, 1)]
    public void CoalescingArithmeticAndCasts_PreserveComposition(string expression, int present, int absent)
    {
        var type = Compile($$"""
            module Grouping Grouping
              module GroupingModule
                func Probe (int?:x) → int
                  !
                  return {{expression}}
            """);
        Assert.Equal(present, Invoke(type, "Probe", 5));
        Assert.Equal(absent, Invoke(type, "Probe", new object?[] { null }));
    }

    [Fact]
    public void CastAndUnaryComposition_PreserveCompleteOperands()
    {
        var type = Compile("""
            module Grouping Grouping
              module GroupingModule
                func CastSum (double:x, double:y) → int
                  !
                  return (int)(x + y)
                func NegatedSum (double:x, double:y) → int
                  !
                  return -(int)(x + y)
                func Character (int:x, int:y) → char
                  !
                  return (char)(x + y)
                func Code (string:a, string:b) → int
                  !
                  return (int)(a + b)[1]
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
            ("(not (and >= 0 <= 10))", x => !(x >= 0 && x <= 10)),
            ("(not (or < 0 > 10))", x => !(x < 0 || x > 10)),
            ("(and (or < 0 > 10) < 20)", x => (x < 0 || x > 10) && x < 20),
            ("(or < 0 (and >= 10 (not > 20)))", x => x < 0 || x >= 10 && !(x > 20)),
            ("(not (not (or < 0 > 10)))", x => !!(x < 0 || x > 10))
        };
        foreach (var (pattern, oracle) in cases)
        {
            var type = Compile($$"""
                module Grouping Grouping
                  module GroupingModule
                    func Probe (int:x) → bool
                      !
                      return match x
                        case {{pattern}} → true
                        case _ → false
                """);
            foreach (var input in Enumerable.Range(-12, 45))
                Assert.Equal(oracle(input), Invoke(type, "Probe", input));
        }
    }

    [Fact]
    public void GroupedReceiversAndCoalescing_EvaluateChildrenOnceInOrder()
    {
        var type = Compile("""
            module Grouping Grouping
              module GroupingModule
                func RecordText private (List<string>:trace, string:value) → string
                  ! mut
                  trace.Add(value)
                  return value
                func RecordNumber private (List<int>:trace, int:value) → int
                  ! mut
                  trace.Add(value)
                  return value
                func Text (List<string>:trace) → string
                  ! mut
                  return (RecordText(trace, "a") + RecordText(trace, "b")).ToUpper()
                func Number (int?:value, List<int>:trace) → int
                  ! mut
                  return (value ?? RecordNumber(trace, 1)) + RecordNumber(trace, 2)
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
    [InlineData(".ConfigureAwait(false)")]
    public async Task AwaitCoalescedTask_AwaitsSelectedReceiver(string configuration)
    {
        var type = Compile($$"""
            module Grouping Grouping
              module GroupingModule
                func Probe async (Task<int>:first, Task<int>:second) → Task<int>
                  return await (first ?? second){{configuration}}
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
