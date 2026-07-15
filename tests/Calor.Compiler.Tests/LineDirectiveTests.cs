using Calor.Compiler;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for #line directive emission (source maps) — Phase 1 item 1 of the
/// agent-native strategy. Generated C# maps statements back to the .calr
/// source so Roslyn diagnostics and stack traces point at Calor code.
/// </summary>
public class LineDirectiveTests
{
    private const string FizzBuzzSource = """
§M{m001:FizzBuzz}
  §F{f001:Main:pub} () -> void
    §E{cw}
    §L{for1:i:1:100:1}
      §IF{if1} (== (% i 15) 0)
        §P "FizzBuzz"
      §EI (== (% i 3) 0)
        §P "Fizz"
      §EL
        §P i
""";

    [Fact]
    public void Compile_WithFilePath_EmitsLineDirectivesByDefault()
    {
        var result = Program.Compile(FizzBuzzSource, "fizzbuzz.calr", new CompilationOptions());

        Assert.False(result.HasErrors);
        // The for loop is on source line 4, the if on line 5, first §P on line 6.
        Assert.Contains("#line 4 \"fizzbuzz.calr\"", result.GeneratedCode);
        Assert.Contains("#line 5 \"fizzbuzz.calr\"", result.GeneratedCode);
        Assert.Contains("#line 6 \"fizzbuzz.calr\"", result.GeneratedCode);
        // Generated-only regions are reset so they attribute to the .g.cs file.
        Assert.Contains("#line default", result.GeneratedCode);
    }

    [Fact]
    public void Compile_WithLineDirectivesDisabled_EmitsNoDirectives()
    {
        var options = new CompilationOptions { EmitLineDirectives = false };
        var result = Program.Compile(FizzBuzzSource, "fizzbuzz.calr", options);

        Assert.False(result.HasErrors);
        Assert.DoesNotContain("#line", result.GeneratedCode);
    }

    [Fact]
    public void Compile_WithoutFilePath_EmitsNoDirectives()
    {
        var result = Program.Compile(FizzBuzzSource, null, new CompilationOptions());

        Assert.False(result.HasErrors);
        Assert.DoesNotContain("#line", result.GeneratedCode);
    }

    [Fact]
    public void Compile_EveryLineMappingIsClosed()
    {
        var result = Program.Compile(FizzBuzzSource, "fizzbuzz.calr", new CompilationOptions());

        Assert.False(result.HasErrors);
        var lines = result.GeneratedCode.Split('\n');
        var opens = lines.Count(l => l.TrimStart().StartsWith("#line ") && !l.TrimStart().StartsWith("#line default"));
        var closes = lines.Count(l => l.TrimStart().StartsWith("#line default"));
        Assert.True(opens > 0);
        Assert.Equal(opens, closes);
    }

    [Fact]
    public void Compile_PathWithBackslashes_IsEscapedInDirective()
    {
        var result = Program.Compile(FizzBuzzSource, "src\\fizzbuzz.calr", new CompilationOptions());

        Assert.False(result.HasErrors);
        Assert.Contains("#line 4 \"src\\\\fizzbuzz.calr\"", result.GeneratedCode);
    }

    [Fact]
    public void Compile_GeneratedCodeWithDirectives_ParsesAsValidCSharp()
    {
        var result = Program.Compile(FizzBuzzSource, "fizzbuzz.calr", new CompilationOptions());

        Assert.False(result.HasErrors);
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(result.GeneratedCode);
        Assert.Empty(tree.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
    }
}
