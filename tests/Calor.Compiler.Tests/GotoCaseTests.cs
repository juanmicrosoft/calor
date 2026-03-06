using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class GotoCaseTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static string Compile(string source, out DiagnosticBag diagnostics)
    {
        var module = Parse(source, out diagnostics);
        if (diagnostics.HasErrors) return "";
        var emitter = new CSharpEmitter();
        return emitter.Emit(module);
    }

    [Fact]
    public void GotoCase_ConvertsSuccessfully()
    {
        var csharp = @"
public class Foo
{
    public int Bar(int x)
    {
        switch (x)
        {
            case 1:
                goto case 2;
            case 2:
                return x;
            default:
                return 0;
        }
    }
}";
        var converter = new CSharpToCalorConverter(new ConversionOptions());
        var result = converter.Convert(csharp, "Test.cs");
        Assert.True(result.Success, string.Join("; ", result.Issues));
        Assert.NotNull(result.CalorSource);
        Assert.True(result.CalorSource.Length > 0);
    }

    [Fact]
    public void GotoDefault_ConvertsSuccessfully()
    {
        var csharp = @"
public class Foo
{
    public int Bar(int x)
    {
        switch (x)
        {
            case 1:
                goto default;
            default:
                return 0;
        }
    }
}";
        var converter = new CSharpToCalorConverter(new ConversionOptions());
        var result = converter.Convert(csharp, "Test.cs");
        Assert.True(result.Success, string.Join("; ", result.Issues));
        Assert.NotNull(result.CalorSource);
        Assert.True(result.CalorSource.Length > 0);
    }

    [Fact]
    public void GotoCase_EmitsNativeCalorSyntax()
    {
        var csharp = @"
public class Foo
{
    public int Bar(int x)
    {
        switch (x)
        {
            case 1:
                goto case 2;
            case 2:
                return x;
            default:
                return 0;
        }
    }
}";
        var converter = new CSharpToCalorConverter(new ConversionOptions());
        var result = converter.Convert(csharp, "Test.cs");
        Assert.True(result.Success, string.Join("; ", result.Issues));
        Assert.Contains("§GOTO{CASE:2}", result.CalorSource!);
        Assert.DoesNotContain("§CSHARP", result.CalorSource!);
    }

    [Fact]
    public void GotoDefault_EmitsNativeCalorSyntax()
    {
        var csharp = @"
public class Foo
{
    public int Bar(int x)
    {
        switch (x)
        {
            case 1:
                goto default;
            default:
                return 0;
        }
    }
}";
        var converter = new CSharpToCalorConverter(new ConversionOptions());
        var result = converter.Convert(csharp, "Test.cs");
        Assert.True(result.Success, string.Join("; ", result.Issues));
        Assert.Contains("§GOTO{DEFAULT}", result.CalorSource!);
        Assert.DoesNotContain("§CSHARP", result.CalorSource!);
    }

    [Fact]
    public void ParseGotoCaseInteger_ProducesCorrectAst()
    {
        // Minimal Calor source with a goto case inside a switch
        var source = @"
§M{m001:Test}
§F{f001:Test:pub}
  §I{i32:x}
  §O{i32}
  §W{sw1} x
    §K 1
      §GOTO{CASE:2}
    §/K
    §K 2
      §R x
    §/K
    §K _
      §R 0
    §/K
  §/W{sw1}
§/F{f001}
§/M{m001}";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors,
            string.Join("\n", diagnostics.Errors.Select(d => d.Message)));
    }

    [Fact]
    public void ParseGotoDefault_ProducesCorrectAst()
    {
        var source = @"
§M{m001:Test}
§F{f001:Test:pub}
  §I{i32:x}
  §O{i32}
  §W{sw1} x
    §K 1
      §GOTO{DEFAULT}
    §/K
    §K _
      §R 0
    §/K
  §/W{sw1}
§/F{f001}
§/M{m001}";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors,
            string.Join("\n", diagnostics.Errors.Select(d => d.Message)));
    }

    [Fact]
    public void GotoCaseInteger_EmitsCSharpCorrectly()
    {
        var source = @"
§M{m001:Test}
§F{f001:Test:pub}
  §I{i32:x}
  §O{i32}
  §W{sw1} x
    §K 1
      §GOTO{CASE:2}
    §/K
    §K 2
      §R x
    §/K
    §K _
      §R 0
    §/K
  §/W{sw1}
§/F{f001}
§/M{m001}";

        var csharp = Compile(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors,
            string.Join("\n", diagnostics.Errors.Select(d => d.Message)));
        Assert.Contains("goto case 2;", csharp);
    }

    [Fact]
    public void GotoDefault_EmitsCSharpCorrectly()
    {
        var source = @"
§M{m001:Test}
§F{f001:Test:pub}
  §I{i32:x}
  §O{i32}
  §W{sw1} x
    §K 1
      §GOTO{DEFAULT}
    §/K
    §K _
      §R 0
    §/K
  §/W{sw1}
§/F{f001}
§/M{m001}";

        var csharp = Compile(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors,
            string.Join("\n", diagnostics.Errors.Select(d => d.Message)));
        Assert.Contains("goto default;", csharp);
    }

    [Fact]
    public void GotoCaseString_EmitsNativeCalorSyntax()
    {
        var csharp = @"
public class Foo
{
    public int Bar(string x)
    {
        switch (x)
        {
            case ""hello"":
                goto case ""world"";
            case ""world"":
                return 1;
            default:
                return 0;
        }
    }
}";
        var converter = new CSharpToCalorConverter(new ConversionOptions());
        var result = converter.Convert(csharp, "Test.cs");
        Assert.True(result.Success, string.Join("; ", result.Issues));
        Assert.Contains("§GOTO{CASE:", result.CalorSource!);
        Assert.DoesNotContain("§CSHARP", result.CalorSource!);
    }
}
