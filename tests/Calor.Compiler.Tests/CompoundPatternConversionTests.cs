using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;
using Xunit.Abstractions;

namespace Calor.Compiler.Tests;

public class CompoundPatternConversionTests
{
    private readonly ITestOutputHelper _output;
    private readonly CSharpToCalorConverter _converter = new();

    public CompoundPatternConversionTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void CompoundPattern_ConvertsSuccessfully()
    {
        var csharp = @"
public class Foo
{
    public string Bar(int x) => x switch
    {
        > 0 and < 100 => ""small"",
        >= 100 or < 0 => ""other"",
        _ => ""zero""
    };
}";
        var result = _converter.Convert(csharp, "Test.cs");

        _output.WriteLine($"Success: {result.Success}");
        _output.WriteLine($"CalorSource:\n{result.CalorSource}");
        foreach (var issue in result.Issues)
            _output.WriteLine($"Issue: {issue.Message}");

        Assert.True(result.Success, string.Join("\n", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.CalorSource);
        Assert.True(result.CalorSource.Length > 10);
    }

    [Fact]
    public void CompoundPattern_RoundTrip_ParsesCalorSource()
    {
        var csharp = @"
public class Foo
{
    public string Bar(int x) => x switch
    {
        > 0 and < 100 => ""small"",
        >= 100 or < 0 => ""other"",
        _ => ""zero""
    };
}";
        var result = _converter.Convert(csharp, "Test.cs");
        Assert.True(result.Success, string.Join("\n", result.Issues.Select(i => i.Message)));

        var calorSource = result.CalorSource!;
        _output.WriteLine($"CalorSource:\n{calorSource}");

        // Try to lex+parse the Calor source
        var diag = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diag);
        var tokens = lexer.TokenizeAll();
        _output.WriteLine($"Token count: {tokens.Count}");

        var parser = new Parser(tokens, diag);
        var module = parser.Parse();
        _output.WriteLine($"Parse succeeded: {module.Classes.Count} classes");

        Assert.True(module.Classes.Count > 0, "Should have at least one class");
    }

    [Fact]
    public void RelationalPattern_ConvertsSuccessfully()
    {
        var csharp = @"
public class Grader
{
    public string GetGrade(int score) => score switch
    {
        >= 90 => ""A"",
        >= 80 => ""B"",
        >= 70 => ""C"",
        _ => ""F""
    };
}";
        var result = _converter.Convert(csharp, "Test.cs");
        Assert.True(result.Success, string.Join("\n", result.Issues.Select(i => i.Message)));

        var calorSource = result.CalorSource!;
        _output.WriteLine($"CalorSource:\n{calorSource}");

        // Round-trip: parse the Calor source
        var diag = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diag);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diag);
        var module = parser.Parse();
        Assert.True(module.Classes.Count > 0);
    }

    [Fact]
    public void CompoundPattern_AST_HasCorrectNodes()
    {
        var csharp = @"
public class Foo
{
    public string Bar(int x) => x switch
    {
        > 0 and < 100 => ""small"",
        >= 100 or < 0 => ""other"",
        _ => ""zero""
    };
}";
        var result = _converter.Convert(csharp, "Test.cs");
        Assert.True(result.Success, string.Join("\n", result.Issues.Select(i => i.Message)));

        var classNode = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(classNode.Methods);
        var returnStmt = Assert.IsType<ReturnStatementNode>(method.Body[0]);
        var matchExpr = Assert.IsType<MatchExpressionNode>(returnStmt.Expression);

        Assert.Equal(3, matchExpr.Cases.Count);

        // First case: > 0 and < 100 → AndPatternNode
        var andPattern = Assert.IsType<AndPatternNode>(matchExpr.Cases[0].Pattern);
        Assert.IsType<RelationalPatternNode>(andPattern.Left);
        Assert.IsType<RelationalPatternNode>(andPattern.Right);

        // Second case: >= 100 or < 0 → OrPatternNode
        var orPattern = Assert.IsType<OrPatternNode>(matchExpr.Cases[1].Pattern);
        Assert.IsType<RelationalPatternNode>(orPattern.Left);
        Assert.IsType<RelationalPatternNode>(orPattern.Right);

        // Third case: _ → WildcardPatternNode
        Assert.IsType<WildcardPatternNode>(matchExpr.Cases[2].Pattern);
    }

    [Fact]
    public void IsPattern_CompoundRelational_ConvertsSuccessfully()
    {
        var csharp = @"
public class Validator
{
    public bool IsInRange(int x)
    {
        return x is > 0 and < 100;
    }
    public bool IsOutOfRange(int x)
    {
        return x is < 0 or > 100;
    }
    public bool IsNotNegative(int x)
    {
        return x is not < 0;
    }
}";
        var result = _converter.Convert(csharp, "Test.cs");

        _output.WriteLine($"Success: {result.Success}");
        _output.WriteLine($"CalorSource:\n{result.CalorSource}");
        foreach (var issue in result.Issues)
            _output.WriteLine($"Issue: {issue.Message}");

        Assert.True(result.Success, string.Join("\n", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.CalorSource);
        Assert.True(result.CalorSource.Length > 10);

        // Round-trip: parse the Calor source
        var diag = new DiagnosticBag();
        var lexer = new Lexer(result.CalorSource, diag);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diag);
        var module = parser.Parse();

        _output.WriteLine($"Parse errors: {diag.Count()}");
        foreach (var d in diag)
            _output.WriteLine($"  Diag: {d}");

        Assert.True(module.Classes.Count > 0, "Should parse back to at least one class");
    }
}
