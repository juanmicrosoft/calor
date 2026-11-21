using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Calor.Enforcement.Tests;
using Xunit;

namespace Calor.Compiler.Tests;

public class NativeCodeGenerationMutationTests
{
    [Fact]
    public void Arithmetic_PreservesDistinctOperands()
    {
        const string source = """
            module Arithmetic
              func Calculate public (int:left, int:right) → int
                !
                return left * 3 + right
            """;

        foreach (var candidate in new[] { source, EmitNative(source) })
        {
            var result = TestHarness.Execute(candidate, "Calculate", [7, 5], new CompilationOptions());
            Assert.Null(result.Exception);
            Assert.Equal(26, result.ReturnValue);
        }
    }

    [Fact]
    public void BinaryOperands_ExecuteOnceInSourceOrder()
    {
        const string source = """
            module OperandOrder
              func Record private (System.Collections.Generic.List<int>:trace, int:value) → int
                trace.Add(value)
                return value
              func Calculate public (System.Collections.Generic.List<int>:trace) → int
                ! mut
                return Record(trace, 1) - Record(trace, 2)
            """;

        foreach (var candidate in new[] { source, EmitNative(source) })
        {
            var trace = new List<int>();
            var result = TestHarness.Execute(candidate, "Calculate", [trace], new CompilationOptions());
            Assert.Null(result.Exception);
            Assert.Equal(-1, result.ReturnValue);
            Assert.Equal([1, 2], trace);
            AssertMutationRequired(candidate, "Calculate");
        }
    }

    [Theory]
    [InlineData(true, 11)]
    [InlineData(false, 22)]
    public void Conditional_ExecutesOnlySelectedArm(bool chooseFirst, int expected)
    {
        const string source = """
            module ConditionalOrder
              func Record private (System.Collections.Generic.List<int>:trace, int:value) → int
                trace.Add(value)
                return value
              func Choose public (System.Collections.Generic.List<int>:trace, bool:first) → int
                ! mut
                return first ? Record(trace, 11) : Record(trace, 22)
            """;

        foreach (var candidate in new[] { source, EmitNative(source) })
        {
            var trace = new List<int>();
            var result = TestHarness.Execute(candidate, "Choose", [trace, chooseFirst], new CompilationOptions());
            Assert.Null(result.Exception);
            Assert.Equal(expected, result.ReturnValue);
            Assert.Equal([expected], trace);
            AssertMutationRequired(candidate, "Choose");
        }
    }

    private static string EmitNative(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var emitted = new CalorEmitter().Emit(module);
        Assert.DoesNotContain('§', emitted);
        Assert.DoesNotContain("raw ", emitted);
        return emitted;
    }

    private static void AssertMutationRequired(string source, string caller)
    {
        var rejected = Program.Compile(source.Replace("! mut", "!", StringComparison.Ordinal),
            filePath: null, options: new CompilationOptions());
        Assert.True(rejected.HasErrors);
        var errors = rejected.Diagnostics.Errors.ToArray();
        Assert.All(errors, diagnostic => Assert.Equal(DiagnosticCode.ForbiddenEffect, diagnostic.Code));
        // Record is private, so it infers its row; only the public caller's declared row is checked.
        Assert.Equal($"Function '{caller}' uses effect 'mut' but does not declare it",
            Assert.Single(errors).Message);
        Assert.DoesNotContain(rejected.Diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }
}
