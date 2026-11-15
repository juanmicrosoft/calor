using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public class ArrowConditionalRuntimeTests
{
    [Fact]
    public void InlineConditionalReturn_DoesNotConsumeMethodDedentBeforeInteropSibling()
    {
        const string source = """
            module Arrow Arrow block
              class C
                func Native static (bool:b) → int
                  !
                  return (b ? 1 : 2)
                raw member:unsafe:Other:test 31
            public static int Other() => 3;
            """;
        var module = Parse(source);
        var type = Assert.Single(module.Classes);
        Assert.Equal("Native", Assert.Single(type.Methods).Name);
        Assert.Contains("Other()", Assert.Single(type.InteropBlocks).CSharpCode);
    }

    [Fact]
    public void BlockReturn_DoesNotStealLoopDedent()
    {
        const string source = """
            module Arrow Arrow block
              func HasNegative (int[]:arr) → bool
                !
                for i from 0 to (arr.Length - 1) step 1
                  if (arr[i] < 0)
                    return true
                return false
            """;
        var module = Parse(source);
        var function = Assert.Single(module.Functions);
        Assert.IsType<ForStatementNode>(function.Body[0]);
        Assert.IsType<ReturnStatementNode>(function.Body[1]);
        var type = Compile(source);
        Assert.Equal(false, Invoke(type, "HasNegative", Array.Empty<int>()));
        Assert.Equal(false, Invoke(type, "HasNegative", new[] { 1, 2, 3 }));
        Assert.Equal(true, Invoke(type, "HasNegative", new[] { 1, 2, -3 }));
    }

    [Theory]
    [InlineData("§IF (< i 0)\n        §R\n")]
    [InlineData("§IF (&&\n        (< i 0)\n        (> i -10))\n        §R\n")]
    [InlineData("§IF (< i 0)\n        §R\n      §EI (&&\n        (> i 9)\n        (< i 20))\n        §R\n")]
    [InlineData("§IF (< i 0)\n        §R\n      §EI (> i 9)\n        §R\n")]
    [InlineData("§IF (< i 0)\n        §R\n      §EL\n        §C{trace.Add} §A i §/C\n")]
    public void FollowingNonReturnStatement_ExecutesOnceAfterLoop(string branch)
    {
        var source = """
            §M{Arrow}
              §F{Probe:pub} (List<i32>:trace) -> void
                §E{mut}
                §L{i:0:2:1}
                  §C{trace.Add} §A i §/C
            """ + "\n      " + branch + "    §C{trace.Add} §A 99 §/C\n";
        var module = Parse(source);
        var function = Assert.Single(module.Functions);
        Assert.Equal(2, function.Body.Count);
        var loop = Assert.IsType<ForStatementNode>(function.Body[0]);
        Assert.Equal(2, loop.Body.Count);
        Assert.IsType<CallStatementNode>(function.Body[1]);
        var expected = branch.Contains("§EL") ? new[] { 0, 0, 1, 1, 2, 2, 99 } : new[] { 0, 1, 2, 99 };
        foreach (var text in new[] { source, new CalorEmitter().Emit(module) })
        {
            var trace = new List<int>();
            Invoke(Compile(text), "Probe", trace);
            Assert.Equal(expected, trace);
        }
    }

    [Fact]
    public void NestedInlineIf_LeavesOuterElseAndNextFunctionIntact()
    {
        var type = Compile("""
            module Arrow Arrow block
              func Probe (int:x) → int
                !
                if (x > 0)
                  if (x > 1)
                    return 2
                else
                  return -1
                return 1

              func Other () → int
                !
                if true
                  return 3
                return 4
            """);
        Assert.Equal(-1, Invoke(type, "Probe", 0));
        Assert.Equal(1, Invoke(type, "Probe", 1));
        Assert.Equal(2, Invoke(type, "Probe", 2));
        Assert.Equal(3, Invoke(type, "Other"));
    }

    [Theory]
    [InlineData("if true → if true →\n        trace.Add(i)")]
    [InlineData("if false → return else →\n        trace.Add(i)")]
    public void MidLineStatementClauses_AreRejected(string branch)
    {
        var source = """
            module Arrow Arrow block
              func Probe (List<int>:trace) → void
                ! mut
                for i from 0 to 2 step 1
            """ + "\n      " + branch + "\n      trace.Add(99)\n";
        var diagnostics = new DiagnosticBag();
        new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Message.Contains("Arrow conditional statements are removed", StringComparison.Ordinal));
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        return module;
    }

    private static object? Invoke(Type type, string name, params object?[] args) =>
        type.GetMethod(name)!.Invoke(null, args);

    private static Type Compile(string source)
    {
        var result = Program.Compile(source, "arrow.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            EnforceEffects = true,
            StatusWriter = TextWriter.Null
        });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var compilation = CSharpCompilation.Create("Arrow_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + result.GeneratedCode)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics));
        return Assembly.Load(stream.ToArray()).GetType("Arrow.ArrowModule", throwOnError: true)!;
    }
}
