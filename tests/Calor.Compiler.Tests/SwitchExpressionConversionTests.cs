using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for C# switch expression to Calor MatchExpressionNode conversion.
/// </summary>
public class SwitchExpressionConversionTests
{
    private readonly CSharpToCalorConverter _converter = new();

    #region Basic Switch Expression

    [Fact]
    public void Convert_SimpleSwitchExpression_CreatesMatchExpression()
    {
        var csharpSource = """
            public class Service
            {
                public string GetDay(int day) => day switch
                {
                    1 => "Monday",
                    2 => "Tuesday",
                    _ => "Unknown"
                };
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);

        // Find the method
        var classNode = Assert.Single(result.Ast.Classes);
        var method = Assert.Single(classNode.Methods);

        // Body should have a return statement with a match expression
        Assert.Single(method.Body);
        var returnStmt = Assert.IsType<ReturnStatementNode>(method.Body[0]);
        var matchExpr = Assert.IsType<MatchExpressionNode>(returnStmt.Expression);

        // Should have 3 cases
        Assert.Equal(3, matchExpr.Cases.Count);
    }

    [Fact]
    public void Convert_SwitchExpression_ConstantPatterns()
    {
        var csharpSource = """
            public class Calculator
            {
                public int Evaluate(string op, int a, int b) => op switch
                {
                    "+" => a + b,
                    "-" => a - b,
                    "*" => a * b,
                    "/" => a / b,
                    _ => 0
                };
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));

        var classNode = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(classNode.Methods);
        var returnStmt = Assert.IsType<ReturnStatementNode>(method.Body[0]);
        var matchExpr = Assert.IsType<MatchExpressionNode>(returnStmt.Expression);

        // 4 constant patterns + 1 wildcard
        Assert.Equal(5, matchExpr.Cases.Count);

        // First 4 should be literal patterns
        for (int i = 0; i < 4; i++)
        {
            Assert.IsType<LiteralPatternNode>(matchExpr.Cases[i].Pattern);
        }

        // Last should be wildcard
        Assert.IsType<WildcardPatternNode>(matchExpr.Cases[4].Pattern);
    }

    [Fact]
    public void Convert_SwitchExpression_DiscardPattern()
    {
        var csharpSource = """
            public class Service
            {
                public string GetMessage(int code) => code switch
                {
                    0 => "Zero",
                    _ => "Other"
                };
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));

        var classNode = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(classNode.Methods);
        var returnStmt = Assert.IsType<ReturnStatementNode>(method.Body[0]);
        var matchExpr = Assert.IsType<MatchExpressionNode>(returnStmt.Expression);

        Assert.Equal(2, matchExpr.Cases.Count);
        Assert.IsType<WildcardPatternNode>(matchExpr.Cases[1].Pattern);
    }

    #endregion

    #region Advanced Patterns

    [Fact]
    public void Convert_SwitchExpression_VarPattern()
    {
        var csharpSource = """
            public class Service
            {
                public string Process(object input) => input switch
                {
                    string s => s.ToUpper(),
                    var other => other.ToString()
                };
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));

        var classNode = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(classNode.Methods);
        var returnStmt = Assert.IsType<ReturnStatementNode>(method.Body[0]);
        var matchExpr = Assert.IsType<MatchExpressionNode>(returnStmt.Expression);

        Assert.Equal(2, matchExpr.Cases.Count);

        // First case: declaration pattern "string s"
        var firstCase = matchExpr.Cases[0];
        Assert.IsType<VarPatternNode>(firstCase.Pattern);

        // Second case: var pattern
        var secondCase = matchExpr.Cases[1];
        Assert.IsType<VarPatternNode>(secondCase.Pattern);
        var varPattern = (VarPatternNode)secondCase.Pattern;
        Assert.Equal("other", varPattern.Name);
    }

    [Fact]
    public void Convert_SwitchExpression_RelationalPattern()
    {
        var csharpSource = """
            public class Grader
            {
                public string GetGrade(int score) => score switch
                {
                    >= 90 => "A",
                    >= 80 => "B",
                    >= 70 => "C",
                    >= 60 => "D",
                    _ => "F"
                };
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));

        var classNode = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(classNode.Methods);
        var returnStmt = Assert.IsType<ReturnStatementNode>(method.Body[0]);
        var matchExpr = Assert.IsType<MatchExpressionNode>(returnStmt.Expression);

        Assert.Equal(5, matchExpr.Cases.Count);

        // First 4 should be relational patterns
        for (int i = 0; i < 4; i++)
        {
            Assert.IsType<RelationalPatternNode>(matchExpr.Cases[i].Pattern);
            var relPattern = (RelationalPatternNode)matchExpr.Cases[i].Pattern;
            Assert.Equal("gte", relPattern.Operator);
        }

        // Last should be wildcard
        Assert.IsType<WildcardPatternNode>(matchExpr.Cases[4].Pattern);
    }

    [Fact]
    public void Convert_SwitchExpression_PropertyPattern()
    {
        var csharpSource = """
            public class Person { public string Name { get; set; } public int Age { get; set; } }
            public class Service
            {
                public string Describe(Person person) => person switch
                {
                    { Age: >= 18 } => "Adult",
                    _ => "Minor"
                };
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));

        // Find the Service class
        var serviceClass = result.Ast!.Classes.FirstOrDefault(c => c.Name == "Service");
        Assert.NotNull(serviceClass);

        var method = Assert.Single(serviceClass.Methods);
        var returnStmt = Assert.IsType<ReturnStatementNode>(method.Body[0]);
        var matchExpr = Assert.IsType<MatchExpressionNode>(returnStmt.Expression);

        Assert.Equal(2, matchExpr.Cases.Count);

        // First case should be a property pattern
        Assert.IsType<PropertyPatternNode>(matchExpr.Cases[0].Pattern);
        var propPattern = (PropertyPatternNode)matchExpr.Cases[0].Pattern;
        Assert.Single(propPattern.Matches);
        Assert.Equal("Age", propPattern.Matches[0].PropertyName);
    }

    #endregion

    #region When Clauses (Guards)

    [Fact]
    public void Convert_SwitchExpression_WithWhenClause()
    {
        var csharpSource = """
            public class Service
            {
                public string Categorize(int value) => value switch
                {
                    var x when x < 0 => "Negative",
                    var x when x == 0 => "Zero",
                    var x when x > 100 => "Large",
                    _ => "Normal"
                };
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));

        var classNode = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(classNode.Methods);
        var returnStmt = Assert.IsType<ReturnStatementNode>(method.Body[0]);
        var matchExpr = Assert.IsType<MatchExpressionNode>(returnStmt.Expression);

        Assert.Equal(4, matchExpr.Cases.Count);

        // First 3 cases should have guards
        for (int i = 0; i < 3; i++)
        {
            Assert.NotNull(matchExpr.Cases[i].Guard);
        }

        // Last case (default) should have no guard
        Assert.Null(matchExpr.Cases[3].Guard);
    }

    #endregion

    #region Round-Trip Tests

    [Fact]
    public void Convert_SwitchExpression_RoundTrip_SimpleCases()
    {
        var csharpSource = """
            public class Service
            {
                public string GetDay(int day) => day switch
                {
                    1 => "Monday",
                    2 => "Tuesday",
                    _ => "Unknown"
                };
            }
            """;

        // Convert C# to Calor
        var toCalorResult = _converter.Convert(csharpSource);
        Assert.True(toCalorResult.Success, GetErrorMessage(toCalorResult));

        // Emit back to C#
        var emitter = new CSharpEmitter();
        var emittedCSharp = emitter.Emit(toCalorResult.Ast!);

        // The emitted code should contain a switch expression
        Assert.Contains("switch", emittedCSharp);
        Assert.Contains("=>", emittedCSharp);
        Assert.Contains("Monday", emittedCSharp);
        Assert.Contains("Tuesday", emittedCSharp);
    }

    [Fact]
    public void Convert_SwitchExpression_RoundTrip_WithRelational()
    {
        var csharpSource = """
            public class Grader
            {
                public string GetGrade(int score) => score switch
                {
                    >= 90 => "A",
                    >= 80 => "B",
                    _ => "F"
                };
            }
            """;

        // Convert C# to Calor
        var toCalorResult = _converter.Convert(csharpSource);
        Assert.True(toCalorResult.Success, GetErrorMessage(toCalorResult));

        // Emit back to C#
        var emitter = new CSharpEmitter();
        var emittedCSharp = emitter.Emit(toCalorResult.Ast!);

        // Should contain relational patterns
        Assert.Contains(">=", emittedCSharp);
        Assert.Contains("90", emittedCSharp);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Convert_SwitchExpression_NullPattern()
    {
        var csharpSource = """
            public class Service
            {
                public string GetLength(string? s) => s switch
                {
                    null => "null",
                    "" => "empty",
                    _ => "has content"
                };
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));

        var classNode = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(classNode.Methods);
        var returnStmt = Assert.IsType<ReturnStatementNode>(method.Body[0]);
        var matchExpr = Assert.IsType<MatchExpressionNode>(returnStmt.Expression);

        Assert.Equal(3, matchExpr.Cases.Count);
    }

    [Fact]
    public void Convert_SwitchExpression_InMethodBody()
    {
        var csharpSource = """
            public class Service
            {
                public void Process(int code)
                {
                    var message = code switch
                    {
                        0 => "Success",
                        _ => "Error"
                    };
                    Console.WriteLine(message);
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));

        var classNode = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(classNode.Methods);

        // Should have 2 statements: bind (var message = ...) and call (Console.WriteLine)
        Assert.Equal(2, method.Body.Count);

        var bindStmt = Assert.IsType<BindStatementNode>(method.Body[0]);
        Assert.Equal("message", bindStmt.Name);

        // The initializer should be a match expression
        Assert.IsType<MatchExpressionNode>(bindStmt.Initializer);
    }

    [Fact]
    public void Convert_SwitchExpression_NestedInTernary()
    {
        var csharpSource = """
            public class Service
            {
                public string Process(bool enabled, int code) =>
                    enabled
                        ? code switch { 0 => "Zero", _ => "Other" }
                        : "Disabled";
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));

        var classNode = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(classNode.Methods);
        var returnStmt = Assert.IsType<ReturnStatementNode>(method.Body[0]);

        // Should be a conditional expression
        var conditional = Assert.IsType<ConditionalExpressionNode>(returnStmt.Expression);

        // WhenTrue should be a match expression
        Assert.IsType<MatchExpressionNode>(conditional.WhenTrue);
    }

    #endregion

    #region Helpers

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Issues.Count > 0)
        {
            return string.Join("\n", result.Issues.Select(i => i.Message));
        }
        return "Conversion failed with no specific error message";
    }

    #endregion
}
