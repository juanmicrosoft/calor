using Opal.Compiler.Ast;
using Opal.Compiler.Migration;
using Xunit;

namespace Opal.Compiler.Tests;

/// <summary>
/// Tests for C# to OPAL conversion, including top-level statements support.
/// </summary>
public class CSharpToOpalConversionTests
{
    private readonly CSharpToOpalConverter _converter = new();

    #region Top-Level Statement Tests

    [Fact]
    public void Convert_TopLevelStatements_CreatesMainFunction()
    {
        var csharpSource = """
            var x = 10;
            var y = 20;
            var sum = x + y;
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);

        // Should have exactly one function (the synthetic main)
        Assert.Single(result.Ast.Functions);

        var mainFunc = result.Ast.Functions[0];
        Assert.Equal("main", mainFunc.Id);
        Assert.Equal("Main", mainFunc.Name);
        Assert.Equal(Visibility.Public, mainFunc.Visibility);

        // Should have 3 statements (3 variable declarations)
        Assert.Equal(3, mainFunc.Body.Count);
    }

    [Fact]
    public void Convert_TopLevelStatements_PreservesUsings()
    {
        var csharpSource = """
            using System;
            using System.Collections.Generic;

            var list = new List<int>();
            Console.WriteLine("Hello");
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);

        // Should have 2 using directives
        Assert.Equal(2, result.Ast.Usings.Count);
        Assert.Contains(result.Ast.Usings, u => u.Namespace == "System");
        Assert.Contains(result.Ast.Usings, u => u.Namespace == "System.Collections.Generic");

        // Should have main function
        Assert.Single(result.Ast.Functions);
    }

    [Fact]
    public void Convert_TopLevelStatements_WithIfStatement()
    {
        var csharpSource = """
            var x = 10;
            if (x > 5)
            {
                Console.WriteLine("Greater");
            }
            else
            {
                Console.WriteLine("Less or equal");
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        Assert.Single(result.Ast.Functions);

        var mainFunc = result.Ast.Functions[0];
        // Should have 2 statements: variable declaration and if statement
        Assert.Equal(2, mainFunc.Body.Count);
        Assert.IsType<BindStatementNode>(mainFunc.Body[0]);
        Assert.IsType<IfStatementNode>(mainFunc.Body[1]);
    }

    [Fact]
    public void Convert_TopLevelStatements_WithForLoop()
    {
        var csharpSource = """
            for (int i = 0; i < 10; i++)
            {
                Console.WriteLine(i);
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        Assert.Single(result.Ast.Functions);

        var mainFunc = result.Ast.Functions[0];
        Assert.Single(mainFunc.Body);
        Assert.IsType<ForStatementNode>(mainFunc.Body[0]);
    }

    [Fact]
    public void Convert_TopLevelStatements_WithForEachLoop()
    {
        var csharpSource = """
            var items = new[] { 1, 2, 3 };
            foreach (var item in items)
            {
                Console.WriteLine(item);
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        Assert.Single(result.Ast.Functions);

        var mainFunc = result.Ast.Functions[0];
        Assert.Equal(2, mainFunc.Body.Count);
        Assert.IsType<BindStatementNode>(mainFunc.Body[0]);
        Assert.IsType<ForeachStatementNode>(mainFunc.Body[1]);
    }

    [Fact]
    public void Convert_TopLevelStatements_WithTryCatch()
    {
        var csharpSource = """
            try
            {
                var x = 10 / 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        Assert.Single(result.Ast.Functions);

        var mainFunc = result.Ast.Functions[0];
        Assert.Single(mainFunc.Body);
        Assert.IsType<TryStatementNode>(mainFunc.Body[0]);
    }

    [Fact]
    public void Convert_TopLevelStatements_MethodCalls()
    {
        var csharpSource = """
            Console.WriteLine("Hello");
            Console.Write("World");
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        Assert.Single(result.Ast.Functions);

        var mainFunc = result.Ast.Functions[0];
        Assert.Equal(2, mainFunc.Body.Count);

        // Console.WriteLine becomes PrintStatementNode
        Assert.IsType<PrintStatementNode>(mainFunc.Body[0]);
        Assert.IsType<PrintStatementNode>(mainFunc.Body[1]);
    }

    [Fact]
    public void Convert_TopLevelStatements_WithLambda()
    {
        var csharpSource = """
            var numbers = new[] { 1, 2, 3, 4, 5 };
            var doubled = numbers.Select(x => x * 2);
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        Assert.Single(result.Ast.Functions);

        var mainFunc = result.Ast.Functions[0];
        Assert.Equal(2, mainFunc.Body.Count);
    }

    [Fact]
    public void Convert_TopLevelStatements_AspNetCorePattern()
    {
        // Simulating ASP.NET Core 6+ minimal API pattern
        var csharpSource = """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();

            var app = builder.Build();

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
            }

            app.UseHttpsRedirection();
            app.UseAuthorization();
            app.MapControllers();
            app.Run();
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        Assert.Single(result.Ast.Functions);

        var mainFunc = result.Ast.Functions[0];
        // Should have multiple statements: 2 variable declarations + method calls + if statement
        Assert.True(mainFunc.Body.Count >= 8, $"Expected at least 8 statements, got {mainFunc.Body.Count}");

        // Verify we have if statement for environment check
        Assert.Contains(mainFunc.Body, s => s is IfStatementNode);
    }

    [Fact]
    public void Convert_TopLevelStatements_RecordsFeatureUsage()
    {
        var csharpSource = """
            var x = 10;
            Console.WriteLine(x);
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.Contains("top-level-statement", result.Context.UsedFeatures);
    }

    [Fact]
    public void Convert_TopLevelStatements_UpdatesStats()
    {
        var csharpSource = """
            var x = 10;
            var y = 20;
            Console.WriteLine(x + y);
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.True(result.Context.Stats.ConvertedNodes > 0);
        Assert.True(result.Context.Stats.StatementsConverted >= 3);
        Assert.Equal(1, result.Context.Stats.MethodsConverted); // The synthetic main
    }

    [Fact]
    public void Convert_NoTopLevelStatements_NoMainFunction()
    {
        var csharpSource = """
            using System;

            namespace TestNamespace
            {
                public class TestClass
                {
                    public void Method()
                    {
                        Console.WriteLine("Hello");
                    }
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);

        // Should have no functions (class methods are inside the class)
        Assert.Empty(result.Ast.Functions);

        // Should have the class
        Assert.Single(result.Ast.Classes);
    }

    [Fact]
    public void Convert_MixedTopLevelAndClasses_HandlesCorrectly()
    {
        // In C# 9+, you can have top-level statements with classes in the same file
        var csharpSource = """
            using System;

            var helper = new Helper();
            Console.WriteLine(helper.GetMessage());

            class Helper
            {
                public string GetMessage() => "Hello";
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);

        // Should have the synthetic main function
        Assert.Single(result.Ast.Functions);
        Assert.Equal("Main", result.Ast.Functions[0].Name);

        // Should have the Helper class
        Assert.Single(result.Ast.Classes);
        Assert.Equal("Helper", result.Ast.Classes[0].Name);
    }

    [Fact]
    public void Convert_TopLevelStatements_EmitsOpalSource()
    {
        var csharpSource = """
            var greeting = "Hello, World!";
            Console.WriteLine(greeting);
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.OpalSource);

        // Should contain the main function marker
        Assert.Contains("main", result.OpalSource);
        Assert.Contains("Main", result.OpalSource);

        // Should contain variable binding
        Assert.Contains("greeting", result.OpalSource);
    }

    [Fact]
    public void Convert_TopLevelStatements_WithWhileLoop()
    {
        var csharpSource = """
            var i = 0;
            while (i < 10)
            {
                Console.WriteLine(i);
                i++;
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        Assert.Single(result.Ast.Functions);

        var mainFunc = result.Ast.Functions[0];
        Assert.Contains(mainFunc.Body, s => s is WhileStatementNode);
    }

    [Fact]
    public void Convert_TopLevelStatements_WithSwitchStatement()
    {
        var csharpSource = """
            var day = 1;
            switch (day)
            {
                case 1:
                    Console.WriteLine("Monday");
                    break;
                case 2:
                    Console.WriteLine("Tuesday");
                    break;
                default:
                    Console.WriteLine("Other");
                    break;
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        Assert.Single(result.Ast.Functions);

        var mainFunc = result.Ast.Functions[0];
        Assert.Contains(mainFunc.Body, s => s is MatchStatementNode);
    }

    [Fact]
    public void Convert_TopLevelStatements_WithThrow()
    {
        var csharpSource = """
            var x = -1;
            if (x < 0)
            {
                throw new ArgumentException("x must be positive");
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        Assert.Single(result.Ast.Functions);

        var mainFunc = result.Ast.Functions[0];
        var ifStmt = mainFunc.Body.OfType<IfStatementNode>().First();
        Assert.Contains(ifStmt.ThenBody, s => s is ThrowStatementNode);
    }

    [Fact]
    public void Convert_SingleTopLevelStatement_CreatesMainWithOneStatement()
    {
        var csharpSource = """Console.WriteLine("Single statement");""";

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        Assert.Single(result.Ast.Functions);

        var mainFunc = result.Ast.Functions[0];
        Assert.Single(mainFunc.Body);
    }

    #endregion

    #region Helper Methods

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Success) return string.Empty;
        return string.Join("\n", result.Issues.Select(i => $"{i.Severity}: {i.Message}"));
    }

    #endregion
}
