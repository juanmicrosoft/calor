using System.Reflection;
using Calor.Compiler.CodeGen;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Conversion.Tests;

/// <summary>
/// #1485 -- C# with empty catch / finally / if / loop bodies followed by a
/// statement converts to Calor whose empty clause is followed by a
/// same-column statement. The Calor parser used to pull that statement into
/// the empty clause, so the round-tripped program behaved differently from
/// the original (witness X-TRYCATCH-01: 2 vs 1). The round-tripped program
/// must now behave exactly like the original.
/// </summary>
public class EmptyClauseRoundTripTests
{
    // 0.25 R0 (#1426) witness X-TRYCATCH-01.
    private const string XTryCatch01 = """
        using System;

        public static class Probe
        {
            private static int counter = 0;

            public static void Work(bool fail)
            {
                if (fail)
                {
                    throw new InvalidOperationException("x");
                }
            }

            public static void Step(bool fail)
            {
                try
                {
                    Work(fail);
                }
                catch (InvalidOperationException)
                {
                }
                counter = counter + 1;
            }

            public static string Run()
            {
                Step(false);
                Step(true);
                return counter.ToString();
            }
        }
        """;

    private const string EmptyBodies = """
        using System;

        public static class Probe
        {
            public static string Run()
            {
                int n = 0;
                try
                {
                    n = n + 1;
                }
                finally
                {
                }
                n = n + 10;
                if (n > 1000)
                {
                }
                n = n + 100;
                for (int i = 0; i < 3; i++)
                {
                }
                n = n + 1000;
                while (n < 0)
                {
                }
                n = n + 10000;
                try
                {
                    throw new InvalidOperationException("x");
                }
                catch (ArgumentException)
                {
                }
                catch (InvalidOperationException)
                {
                }
                n = n + 100000;
                return n.ToString();
            }
        }
        """;

    [Theory]
    [InlineData("X-TRYCATCH-01", XTryCatch01, "2")]
    [InlineData("empty-bodies", EmptyBodies, "111111")]
    public void RoundTrippedProgram_BehavesLikeTheOriginal(string name, string csharp, string expected)
    {
        Assert.Equal(expected, Run(csharp, $"original_{name}"));

        var result = TestHelpers.FullRoundTrip(csharp, "EmptyClause");
        Assert.True(result.ConversionSuccess, string.Join("; ", result.ConversionIssues));
        Assert.True(result.CalorParseSuccess,
            string.Join("\n", result.CalorDiagnostics) + "\n" + result.CalorSource);
        Assert.True(result.RoslynSuccess, string.Join("\n", result.RoslynErrors));

        Assert.Equal(expected, Run(result.EmittedCSharp!, $"roundtrip_{name}"));
    }

    private static string Run(string csharp, string assemblyName)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            assemblyName + "_" + Guid.NewGuid().ToString("N"),
            [
                CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble, parseOptions),
                CSharpSyntaxTree.ParseText(csharp, parseOptions),
            ],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join("\n",
            emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)) + "\n" + csharp);

        var assembly = Assembly.Load(stream.ToArray());
        var probe = Assert.Single(assembly.GetTypes(), t => t.Name == "Probe");
        return (string)probe.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!;
    }
}
