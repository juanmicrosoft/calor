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
    public void Migration_PreservesLoopObservations(string body, string expected, bool native)
    {
        var source = $$"""
            public static class Migrated
            {
                public static string Trace = "";
                private static int Bound() { Trace += "B"; return 3; }
                private static int Step() { Trace += "S"; return 1; }
                private static int ExplodingBound()
                {
                    Trace += "B";
                    if (Trace.Length == 3) throw new System.InvalidOperationException();
                    return 3;
                }
                public static int Probe() { {{body}} }
            }
            """;
        var conversion = new CSharpToCalorConverter().Convert(source);
        Assert.True(conversion.Success, string.Join("; ", conversion.Issues.Select(issue => issue.Message)));
        Assert.DoesNotContain(conversion.Losses, loss => loss.Kind == ConversionLossKind.Dropped);
        if (native)
            Assert.Contains("§L{", conversion.CalorSource);
        else
            Assert.Contains(conversion.Losses, loss =>
                loss.Kind == ConversionLossKind.InteropPreserved && loss.Feature == "for");
        var compiled = Program.Compile(conversion.CalorSource!, "for.calr", new CompilationOptions
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
        var compilation = CSharpCompilation.Create("ForOracle_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        Assert.True(emit.Success, string.Join("; ", emit.Diagnostics) + "\n" + source);
        var type = Assembly.Load(image.ToArray()).GetTypes().Single(type => type.Name == "Migrated");
        string? result;
        try
        {
            result = type.GetMethod("Probe")!.Invoke(null, null)!.ToString();
        }
        catch (TargetInvocationException exception)
        {
            result = "throws:" + exception.InnerException!.GetType().Name;
        }
        var trace = (string)type.GetField("Trace")!.GetValue(null)!;
        return result + (trace.Length > 0 ? ":" + trace : "");
    }
}
