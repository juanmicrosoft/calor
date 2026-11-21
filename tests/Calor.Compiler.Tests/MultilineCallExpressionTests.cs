using System.Diagnostics;
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

public class MultilineCallExpressionTests
{
    [Theory]
    [InlineData("let int:value = Math.Max(\n  3,\n  7)\nreturn value", 7)]
    [InlineData("return Math.Max(\n  3,\n  7)", 7)]
    [InlineData("return (1 + Math.Max(\n  3,\n  7))", 8)]
    [InlineData("return Math.Clamp(\n  value: 7,\n  min: 0,\n  max: 5)", 5)]
    [InlineData("let int:value = Math.Max(\n  3,\n  7\n)\nreturn value", 7)]
    [InlineData("return Math.Max(\n  3,\n  7\n)", 7)]
    [InlineData("let int:value = Math.Max(3,\n  7)\nreturn value", 7)]
    [InlineData("let int:value = Math.Max(\n  3, 7)\nreturn value", 7)]
    [InlineData("let int:value = Math.Max(\n3,\n7)\nreturn value", 7)]
    [InlineData("return Math.Max(\n  Math.Min(\n    8,\n    3),\n  7)", 7)]
    [InlineData("return Math.Max(\n  (1 + Math.Min(\n    8,\n    3)),\n  7)", 7)]
    public void Arguments_CompileExecuteAndRoundTrip(string body, int expected)
    {
        var source = ProgramSource(body);
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        foreach (var text in new[] { source, new CalorEmitter().Emit(module) })
            Assert.Equal(expected, Compile(text).GetMethod("Probe")!.Invoke(null, null));
    }

    [Fact]
    public void NestedArgumentLists_LeaveLoopAndFollowingFunctionIntact()
    {
        const string source = """
            module Calls Calls block
              func Probe (List<int>:trace) → void
                ! mut
                for i from 0 to 2 step 1
                  trace.Add(Math.Max(i, 1))
                trace.Add(99)

              func Other () → int
                !
                return 42
            """;
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        Assert.Equal(2, module.Functions.Count);
        Assert.Equal(2, module.Functions[0].Body.Count);
        foreach (var text in new[] { source, new CalorEmitter().Emit(module) })
        {
            var type = Compile(text);
            var trace = new List<int>();
            type.GetMethod("Probe")!.Invoke(null, [trace]);
            Assert.Equal(new[] { 1, 1, 2, 99 }, trace);
            Assert.Equal(42, type.GetMethod("Other")!.Invoke(null, null));
        }
    }

    [Fact]
    public void ElidedHeaderAndLispCalls_LeaveStatementsAndSiblingOperandsIntact()
    {
        const string source = """
            module Calls Calls block
              func Truth () → bool
                !
                return true

              func Text () → string
                !
                return "12"

              func Never () → bool
                !
                return false

              func One () → int
                !
                return 1

              func Probe () → int
                !
                var int:value = 0
                each ch in Text()
                  if Truth()
                    set value (value + 1)
                while Never()
                  Math.Abs(9)
                return (value + (One() + 3))
            """;
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        Assert.Equal(4, module.Functions.Last().Body.Count);
        foreach (var text in new[] { source, new CalorEmitter().Emit(module) })
            Assert.Equal(6, Compile(text).GetMethod("Probe")!.Invoke(null, null));
    }

    [Fact]
    public void ElidedZeroArgumentCall_DoesNotTakeConstructorSiblingArguments()
    {
        const string source = """
            module Calls Calls block
              func One () → int
                !
                return 1

              func Probe () → int
                ! alloc
                let Tuple<int, int>:pair = new Tuple<int, int>(One(), 2)
                return ((pair.Item1 * 10) + pair.Item2)
            """;
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        foreach (var text in new[] { source, new CalorEmitter().Emit(module) })
        {
            var reparsed = Parse(text, out diagnostics);
            Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
            var initializers = reparsed.Functions[1].Body.OfType<BindStatementNode>()
                .Select(binding => binding.Initializer).ToArray();
            var constructor = Assert.Single(initializers.OfType<NewExpressionNode>());
            Assert.Equal(2, constructor.Arguments.Count);
            var call = constructor.Arguments[0] as CallExpressionNode
                ?? Assert.Single(initializers.OfType<CallExpressionNode>());
            Assert.Equal("One", call.Target);
            Assert.Empty(call.Arguments);
            Assert.Equal(2, Assert.IsType<IntLiteralNode>(constructor.Arguments[1]).Value);
        }
    }

    [Fact]
    public void ExplicitInnerArgumentList_KeepsSameIndentContinuationArguments()
    {
        const string source = """
            module Calls Calls block
              func Probe () → int
                ! alloc
                let Tuple<int, int>:pair = new Tuple<int, int>(Math.Max(3, 7), 9)
                return pair.Item1
            """;
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        foreach (var text in new[] { source, new CalorEmitter().Emit(module) })
        {
            var reparsed = Parse(text, out diagnostics);
            Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
            var initializers = reparsed.Functions.Single().Body.OfType<BindStatementNode>()
                .Select(binding => binding.Initializer).ToArray();
            var constructor = Assert.Single(initializers.OfType<NewExpressionNode>());
            Assert.Equal(2, constructor.Arguments.Count);
            // A dotted native call is an expression call on a member access (`Math.Max`).
            var call = Assert.IsType<ExpressionCallNode>(constructor.Arguments[0]);
            var member = Assert.IsType<FieldAccessNode>(call.TargetExpression);
            Assert.Equal("Max", member.FieldName);
            Assert.Equal("Math", Assert.IsType<ReferenceNode>(member.Target).Name);
            Assert.Equal(new[] { 3L, 7L }, call.Arguments.Select(arg => Assert.IsType<IntLiteralNode>(arg).Value));
            Assert.Equal(9, Assert.IsType<IntLiteralNode>(constructor.Arguments[1]).Value);
        }
    }

    [Fact]
    public void ManyCallsOnOneLine_DoNotRepeatedlyScanTheirContainingLine()
    {
        var source = ProgramSource("return (" + string.Join(" + ",
            Enumerable.Repeat("One()", 20_000)) + ")");
        var stopwatch = Stopwatch.StartNew();
        var module = Parse(source, out var diagnostics);
        stopwatch.Stop();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        Assert.IsType<ReturnStatementNode>(Assert.Single(module.Functions.Single().Body));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Parsing a 200 KB call expression took {stopwatch.Elapsed}; containing-line lookup must be amortized linear.");
    }

    [Fact]
    public void OriginalFileReadRepro_ExecutesAndRoundTrips()
    {
        const string source = """
            module Calls Calls block
              func ReadFile (string:path) → string
                ! fs:r
                let string:content = File.ReadAllText(path)
                return content
            """;
        var path = Path.Combine(AppContext.BaseDirectory, $"multiline-call-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, "multiline arguments preserve the file path");
            var module = Parse(source, out var diagnostics);
            Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
            foreach (var text in new[] { source, new CalorEmitter().Emit(module) })
                Assert.Equal("multiline arguments preserve the file path",
                    Compile(text).GetMethod("ReadFile")!.Invoke(null, [path]));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string ProgramSource(string body) =>
        "module Calls Calls block\n"
        + "  func Probe () → int\n"
        + "    !\n"
        + string.Join("\n", body.Split('\n').Select(line => "    " + line)) + "\n";

    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        return new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
    }

    private static Type Compile(string source)
    {
        var result = Program.Compile(source, "calls.calr");
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Errors));
        var compilation = CSharpCompilation.Create("Calls_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + result.GeneratedCode)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join("\n", emission.Diagnostics));
        return Assembly.Load(stream.ToArray()).GetType("Calls.CallsModule", throwOnError: true)!;
    }
}
