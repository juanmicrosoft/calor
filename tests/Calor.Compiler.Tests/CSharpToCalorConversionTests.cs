using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for C# to Calor conversion, including top-level statements support.
/// </summary>
public class CSharpToCalorConversionTests
{
    private readonly CSharpToCalorConverter _converter = new();

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
    public void Convert_TopLevelStatements_EmitsCalorSource()
    {
        var csharpSource = """
            var greeting = "Hello, World!";
            Console.WriteLine(greeting);
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);

        // Should contain the main function marker
        Assert.Contains("main", result.CalorSource);
        Assert.Contains("Main", result.CalorSource);

        // Should contain variable binding
        Assert.Contains("greeting", result.CalorSource);
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

    #region Object Initializer Tests

    [Fact]
    public void Convert_ObjectInitializer_PreservesPropertyAssignments()
    {
        var csharpSource = """
            var person = new Person { Name = "John", Age = 30, City = "NYC" };
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("§NEW{Person}", result.CalorSource);
        Assert.Contains("Name =", result.CalorSource);
        Assert.Contains("Age =", result.CalorSource);
        Assert.Contains("City =", result.CalorSource);
        Assert.Contains("object-initializer", result.Context.UsedFeatures);
    }

    [Fact]
    public void Convert_ObjectInitializer_WithConstructorArgs()
    {
        var csharpSource = """
            var config = new Settings("default") { Timeout = 30, Enabled = true };
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("§NEW{Settings}", result.CalorSource);
        Assert.Contains("Timeout =", result.CalorSource);
        Assert.Contains("Enabled =", result.CalorSource);
    }

    [Fact]
    public void Convert_ObjectInitializer_NestedObjects()
    {
        var csharpSource = """
            var order = new Order
            {
                Customer = new Customer { Name = "Alice" },
                Total = 100.00
            };
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("Order", result.CalorSource);
        Assert.Contains("Customer =", result.CalorSource);
        Assert.Contains("Total =", result.CalorSource);
    }

    #endregion

    #region Class Modifier Tests

    [Fact]
    public void Convert_PartialClass_PreservesModifier()
    {
        var csharpSource = """
            public partial class MyClass
            {
                public void Method() { }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        Assert.Single(result.Ast.Classes);
        Assert.True(result.Ast.Classes[0].IsPartial);
        Assert.Contains("partial-class", result.Context.UsedFeatures);
    }

    [Fact]
    public void Convert_StaticClass_PreservesModifier()
    {
        var csharpSource = """
            public static class Utilities
            {
                public static int Add(int a, int b) => a + b;
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        Assert.Single(result.Ast.Classes);
        Assert.True(result.Ast.Classes[0].IsStatic);
        Assert.Contains("static-class", result.Context.UsedFeatures);
    }

    [Fact]
    public void Convert_InternalStaticPartialClass_PreservesAllModifiers()
    {
        var csharpSource = """
            internal static partial class Helper
            {
                public static void DoSomething() { }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        Assert.Single(result.Ast.Classes);
        var cls = result.Ast.Classes[0];
        Assert.True(cls.IsPartial);
        Assert.True(cls.IsStatic);
        Assert.Contains("partial", result.CalorSource);
        Assert.Contains("stat", result.CalorSource);
    }

    #endregion

    #region Event Subscription Tests

    [Fact]
    public void Convert_EventSubscription_CreatesSubscribeNode()
    {
        var csharpSource = """
            public class Handler
            {
                public void Setup(Button button)
                {
                    button.Click += OnClick;
                }

                private void OnClick(object sender, EventArgs e) { }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("§SUB", result.CalorSource);
        Assert.Contains("+=", result.CalorSource);
        Assert.Contains("event-subscribe", result.Context.UsedFeatures);
    }

    [Fact]
    public void Convert_EventUnsubscription_CreatesUnsubscribeNode()
    {
        var csharpSource = """
            public class Handler
            {
                public void Cleanup(Button button)
                {
                    button.Click -= OnClick;
                }

                private void OnClick(object sender, EventArgs e) { }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("§UNSUB", result.CalorSource);
        Assert.Contains("-=", result.CalorSource);
        Assert.Contains("event-unsubscribe", result.Context.UsedFeatures);
    }

    [Fact]
    public void Convert_EventSubscriptionWithLambda_Handled()
    {
        var csharpSource = """
            public class Handler
            {
                public void Setup(Button button)
                {
                    button.Click += (s, e) => Console.WriteLine("Clicked");
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("§SUB", result.CalorSource);
    }

    #endregion

    #region Continue Statement Tests

    [Fact]
    public void Convert_ContinueInForEach_PreservesStatement()
    {
        var csharpSource = """
            var items = new[] { 1, 2, 3, 4, 5 };
            foreach (var item in items)
            {
                if (item % 2 == 0)
                    continue;
                Console.WriteLine(item);
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("§CN", result.CalorSource);
        Assert.Contains("continue", result.Context.UsedFeatures);
    }

    [Fact]
    public void Convert_ContinueInForLoop_PreservesStatement()
    {
        var csharpSource = """
            for (int i = 0; i < 10; i++)
            {
                if (i == 5)
                    continue;
                Console.WriteLine(i);
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("§CN", result.CalorSource);
    }

    [Fact]
    public void Convert_ContinueInWhileLoop_PreservesStatement()
    {
        var csharpSource = """
            int i = 0;
            while (i < 10)
            {
                i++;
                if (i % 2 == 0)
                    continue;
                Console.WriteLine(i);
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("§CN", result.CalorSource);
    }

    [Fact]
    public void Convert_BreakInForLoop_PreservesStatement()
    {
        var csharpSource = """
            for (int i = 0; i < 100; i++)
            {
                if (i > 10)
                    break;
                Console.WriteLine(i);
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("§BK", result.CalorSource);
        Assert.Contains("break", result.Context.UsedFeatures);
    }

    #endregion

    #region Property Default Value Tests

    [Fact]
    public void Convert_PropertyWithDefaultValue_PreservesInitializer()
    {
        var csharpSource = """
            public class Config
            {
                public string Name { get; set; } = "";
                public int Count { get; set; } = 0;
                public bool Enabled { get; set; } = true;
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        Assert.Single(result.Ast.Classes);

        var cls = result.Ast.Classes[0];
        Assert.Equal(3, cls.Properties.Count);

        // Verify properties have default values
        var nameProp = cls.Properties.First(p => p.Name == "Name");
        Assert.NotNull(nameProp.DefaultValue);

        var countProp = cls.Properties.First(p => p.Name == "Count");
        Assert.NotNull(countProp.DefaultValue);

        var enabledProp = cls.Properties.First(p => p.Name == "Enabled");
        Assert.NotNull(enabledProp.DefaultValue);
    }

    [Fact]
    public void Convert_PropertyDefaultValue_EmittedCorrectly()
    {
        var csharpSource = """
            public class Person
            {
                public string Name { get; set; } = "Unknown";
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("§PROP", result.CalorSource);
        Assert.Contains("= \"Unknown\"", result.CalorSource);
    }

    [Fact]
    public void Convert_PropertyWithStringEmptyDefault_Preserved()
    {
        var csharpSource = """
            public class Data
            {
                public string Value { get; set; } = string.Empty;
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        var prop = result.Ast.Classes[0].Properties[0];
        Assert.NotNull(prop.DefaultValue);
    }

    #endregion

    #region Event Definition Tests

    [Fact]
    public void Convert_EventFieldDeclaration_PreservesInClass()
    {
        var csharpSource = """
            public class EventSource
            {
                public event EventHandler? OnCompleted;
                public event EventHandler<string>? OnMessage;
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        Assert.Single(result.Ast.Classes);

        var cls = result.Ast.Classes[0];
        Assert.Equal(2, cls.Events.Count);
        Assert.Contains(cls.Events, e => e.Name == "OnCompleted");
        Assert.Contains(cls.Events, e => e.Name == "OnMessage");
        Assert.Contains("event-definition", result.Context.UsedFeatures);
    }

    [Fact]
    public void Convert_EventFieldDeclaration_EmittedCorrectly()
    {
        var csharpSource = """
            public class MyService
            {
                public event EventHandler<string>? SpeakRequest;
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        // Events are emitted as fields since the parser doesn't support §EVT in class bodies
        Assert.Contains("§FLD", result.CalorSource);
        Assert.Contains("SpeakRequest", result.CalorSource);
    }

    #endregion

    #region Compound Assignment Tests

    [Fact]
    public void Convert_CompoundAddAssignment_CreatesCompoundNode()
    {
        var csharpSource = """
            public class Counter
            {
                private int _count;

                public void Add(int value)
                {
                    _count += value;
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("compound-assignment", result.Context.UsedFeatures);
        // Should emit as §ASSIGN _count (+ _count value)
        Assert.Contains("§ASSIGN", result.CalorSource);
        Assert.Contains("(+", result.CalorSource);
    }

    [Fact]
    public void Convert_CompoundSubtractAssignment_CreatesCompoundNode()
    {
        var csharpSource = """
            public class Counter
            {
                private int _count;

                public void Subtract(int value)
                {
                    _count -= value;
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("compound-assignment", result.Context.UsedFeatures);
        Assert.Contains("(-", result.CalorSource);
    }

    [Fact]
    public void Convert_CompoundMultiplyAssignment_CreatesCompoundNode()
    {
        var csharpSource = """
            public class Calculator
            {
                private double _value;

                public void Multiply(double factor)
                {
                    _value *= factor;
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("compound-assignment", result.Context.UsedFeatures);
        Assert.Contains("(*", result.CalorSource);
    }

    [Fact]
    public void Convert_CompoundDivideAssignment_CreatesCompoundNode()
    {
        var csharpSource = """
            public class Calculator
            {
                private double _value;

                public void Divide(double divisor)
                {
                    _value /= divisor;
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("compound-assignment", result.Context.UsedFeatures);
        Assert.Contains("(/", result.CalorSource);
    }

    #endregion

    #region Using Statement Tests

    [Fact]
    public void Convert_UsingStatement_CreatesUsingNode()
    {
        var csharpSource = """
            public class FileReader
            {
                public string Read(string path)
                {
                    using (var reader = new StreamReader(path))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        // Using statements are now converted to §USE blocks
        Assert.Contains("§USE{", result.CalorSource);
        Assert.Contains("§/USE{", result.CalorSource);
        Assert.Contains("reader", result.CalorSource);
        Assert.Contains("using-statement", result.Context.UsedFeatures);
    }

    [Fact]
    public void Convert_UsingStatementWithType_PreservesType()
    {
        var csharpSource = """
            public class FileWriter
            {
                public void Write(string path, string content)
                {
                    using (StreamWriter writer = new StreamWriter(path))
                    {
                        writer.Write(content);
                    }
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        // Using statements are now converted to §USE blocks with type
        Assert.Contains("§USE{", result.CalorSource);
        Assert.Contains("writer", result.CalorSource);
        Assert.Contains("StreamWriter", result.CalorSource);
    }

    [Fact]
    public void Convert_UsingStatementNested_HandlesBoth()
    {
        var csharpSource = """
            public class FileCopier
            {
                public void Copy(string source, string dest)
                {
                    using (var reader = new StreamReader(source))
                    {
                        using (var writer = new StreamWriter(dest))
                        {
                            writer.Write(reader.ReadToEnd());
                        }
                    }
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        // Should have two §USE blocks (one per using)
        var useCount = result.CalorSource.Split("§USE{").Length - 1;
        Assert.Equal(2, useCount);
    }

    #endregion

    #region Null-Conditional Operator Tests

    [Fact]
    public void Convert_NullConditionalMemberAccess_NoDoubleDot()
    {
        var csharpSource = """
            public class StatusChecker
            {
                public string? GetStatus(object? obj)
                {
                    return obj?.ToString();
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        // Should NOT contain double dot (?.. or ..)
        Assert.DoesNotContain("?..", result.CalorSource);
        Assert.DoesNotContain("..", result.CalorSource.Replace("...", "")); // Ignore spread operator
        Assert.Contains("null-conditional", result.Context.UsedFeatures);
    }

    [Fact]
    public void Convert_NullConditionalPropertyAccess_CorrectSyntax()
    {
        var csharpSource = """
            public class Checker
            {
                public int? GetLength(string? text)
                {
                    return text?.Length;
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("?.Length", result.CalorSource);
        Assert.DoesNotContain("?..Length", result.CalorSource);
    }

    #endregion

    #region Do-While Loop Tests

    [Fact]
    public void Convert_TopLevelStatements_WithDoWhileLoop()
    {
        var csharpSource = """
            int i = 0;
            do
            {
                Console.WriteLine(i);
                i++;
            } while (i < 5);
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        Assert.Single(result.Ast.Functions);

        var mainFunc = result.Ast.Functions[0];
        // Should have 2 statements: variable declaration and do-while
        Assert.Equal(2, mainFunc.Body.Count);
        Assert.IsType<BindStatementNode>(mainFunc.Body[0]);
        Assert.IsType<DoWhileStatementNode>(mainFunc.Body[1]);

        var doWhile = (DoWhileStatementNode)mainFunc.Body[1];
        Assert.NotEmpty(doWhile.Body);
        Assert.NotNull(doWhile.Condition);
    }

    [Fact]
    public void Convert_DoWhileLoop_RecordsFeatureUsage()
    {
        var csharpSource = """
            do { } while (true);
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.Contains("do-while", result.Context.UsedFeatures);
    }

    [Fact]
    public void Convert_DoWhileLoop_GeneratesCorrectCalor()
    {
        var csharpSource = """
            int i = 0;
            do
            {
                i++;
            } while (i < 10);
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("§DO", result.CalorSource);
        Assert.Contains("§/DO", result.CalorSource);
    }

    #endregion

    #region Fallback Tests

    [Fact]
    public void Convert_UnsupportedExpression_EmitsFallbackNode_WhenGracefulFallbackEnabled()
    {
        var csharpSource = """
            public class Test
            {
                void M()
                {
                    var x = stackalloc int[10];
                }
            }
            """;

        var converter = new CSharpToCalorConverter(new ConversionOptions { GracefulFallback = true });
        var result = converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("§ERR{\"TODO: unknown-expression", result.CalorSource);
    }

    [Fact]
    public void Convert_UnsupportedExpression_FailsConversion_WhenGracefulFallbackDisabled()
    {
        var csharpSource = """
            public class Test
            {
                void M()
                {
                    var x = stackalloc int[10];
                }
            }
            """;

        var converter = new CSharpToCalorConverter(new ConversionOptions { GracefulFallback = false });
        var result = converter.Convert(csharpSource);

        Assert.False(result.Success);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Issues, i =>
            i.Severity == ConversionIssueSeverity.Error &&
            i.Message.Contains("unknown-expression"));
    }

    [Fact]
    public void Convert_UnsupportedExpression_RecordsInExplanation()
    {
        var csharpSource = """
            public class Test
            {
                void M()
                {
                    var x = stackalloc int[10];
                    var y = stackalloc byte[20];
                }
            }
            """;

        var converter = new CSharpToCalorConverter(new ConversionOptions { Explain = true });
        var result = converter.Convert(csharpSource);

        Assert.True(result.Success);
        var explanation = result.Context.GetExplanation();

        Assert.True(explanation.TotalUnsupportedCount >= 2);
        Assert.Contains("unknown-expression", explanation.UnsupportedFeatures.Keys);
    }

    [Fact]
    public void Convert_FallbackExpression_DoesNotContainInlineComments()
    {
        // Inline /* */ comments break the Calor parser - verify they're not emitted
        var csharpSource = """
            public class Test
            {
                void M()
                {
                    var x = stackalloc int[10];
                }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharpSource);

        Assert.True(result.Success);
        Assert.DoesNotContain("/*", result.CalorSource);
        Assert.DoesNotContain("*/", result.CalorSource);
    }

    [Fact]
    public void Convert_FallbackExpression_RoundtripsSuccessfully()
    {
        var csharpSource = """
            public class Test
            {
                void M()
                {
                    var x = stackalloc int[10];
                }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var conversionResult = converter.Convert(csharpSource);

        Assert.True(conversionResult.Success, GetErrorMessage(conversionResult));

        // Compile the Calor source back to C#
        var compilationResult = Program.Compile(conversionResult.CalorSource!);

        Assert.False(compilationResult.HasErrors,
            $"Roundtrip failed:\n{string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message))}");
    }

    [Fact]
    public void Convert_ComplexPattern_EmitsWildcardFallback()
    {
        var csharpSource = """
            public class Test
            {
                void M(object o)
                {
                    var result = o switch
                    {
                        string s and { Length: > 5 } => "long",
                        _ => "other"
                    };
                }
            }
            """;

        var converter = new CSharpToCalorConverter(new ConversionOptions { Explain = true });
        var result = converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);

        // Should emit wildcard pattern, not raw C# text
        Assert.DoesNotContain("string s and", result.CalorSource);
        Assert.Contains("§K _", result.CalorSource);

        // Should record in explanation
        var explanation = result.Context.GetExplanation();
        Assert.Contains("binary pattern (and/or)", explanation.UnsupportedFeatures.Keys);
    }

    [Fact]
    public void Convert_ComplexPattern_RoundtripsSuccessfully()
    {
        var csharpSource = """
            public class Test
            {
                void M(object o)
                {
                    var result = o switch
                    {
                        string s and { Length: > 5 } => "long",
                        int i when i > 0 => "positive",
                        _ => "other"
                    };
                }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var conversionResult = converter.Convert(csharpSource);

        Assert.True(conversionResult.Success, GetErrorMessage(conversionResult));

        // Compile the Calor source back to C#
        var compilationResult = Program.Compile(conversionResult.CalorSource!);

        Assert.False(compilationResult.HasErrors,
            $"Roundtrip failed:\n{string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message))}");
    }

    #endregion

    #region Constructor Argument Preservation Tests

    [Fact]
    public void Convert_ListWithConstructorArg_PreservesArgument()
    {
        var csharpSource = """
            public class Test
            {
                public void Run()
                {
                    var existing = new List<int> { 1, 2, 3 };
                    var copy = new List<int>(existing);
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        // Constructor arg version should use §NEW, not §LIST
        Assert.Contains("§NEW", result.CalorSource);
    }

    [Fact]
    public void Convert_ListWithCapacityArg_PreservesArgument()
    {
        var csharpSource = """
            public class Test
            {
                public void Run()
                {
                    var list = new List<int>(100);
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("§NEW", result.CalorSource);
    }

    [Fact]
    public void Convert_DictionaryWithConstructorArg_PreservesArgument()
    {
        var csharpSource = """
            public class Test
            {
                public void Run()
                {
                    var old = new Dictionary<string, int>();
                    var copy = new Dictionary<string, int>(old);
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        // The copy with ctor arg should use §NEW
        Assert.Contains("§NEW", result.CalorSource);
    }

    [Fact]
    public void Convert_ListWithInitializerNoArgs_StillUsesListCreation()
    {
        var csharpSource = """
            public class Test
            {
                public void Run()
                {
                    var list = new List<int> { 1, 2, 3 };
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        // No-arg initializer should still use §LIST (regression check)
        Assert.Contains("§LIST{", result.CalorSource);
    }

    [Fact]
    public void Convert_HashSetWithConstructorArg_PreservesArgument()
    {
        var csharpSource = """
            public class Test
            {
                public void Run()
                {
                    var existing = new List<int> { 1, 2, 3 };
                    var set = new HashSet<int>(existing);
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("§NEW", result.CalorSource);
    }

    #endregion

    #region Using Statement Round-Trip Tests

    [Fact]
    public void Roundtrip_UsingStatement_CSharpToCalorToCSharp()
    {
        var csharpSource = """
            public class FileProcessor
            {
                public void Process(string path)
                {
                    using (var reader = new StreamReader(path))
                    {
                        Console.WriteLine(reader.ReadToEnd());
                    }
                }
            }
            """;

        // Step 1: C# -> Calor
        var conversionResult = _converter.Convert(csharpSource);
        Assert.True(conversionResult.Success, GetErrorMessage(conversionResult));
        Assert.NotNull(conversionResult.CalorSource);
        Assert.Contains("§USE{", conversionResult.CalorSource);
        Assert.Contains("§/USE{", conversionResult.CalorSource);

        // Step 2: Calor -> C#
        var compilationResult = Program.Compile(conversionResult.CalorSource!);
        Assert.False(compilationResult.HasErrors,
            $"Roundtrip failed:\n{string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message))}");

        // Step 3: Verify the generated C# contains using statement
        Assert.Contains("using (", compilationResult.GeneratedCode);
        Assert.Contains("reader", compilationResult.GeneratedCode);
    }

    [Fact]
    public void Roundtrip_UsingStatementWithType_PreservesType()
    {
        var csharpSource = """
            public class FileProcessor
            {
                public void Process(string path)
                {
                    using (StreamWriter writer = new StreamWriter(path))
                    {
                        writer.Write("test");
                    }
                }
            }
            """;

        // C# -> Calor
        var conversionResult = _converter.Convert(csharpSource);
        Assert.True(conversionResult.Success, GetErrorMessage(conversionResult));
        Assert.NotNull(conversionResult.CalorSource);

        // Calor -> C#
        var compilationResult = Program.Compile(conversionResult.CalorSource!);
        Assert.False(compilationResult.HasErrors,
            $"Roundtrip failed:\n{string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message))}");

        // Verify type is preserved through the round-trip
        Assert.Contains("using (StreamWriter writer =", compilationResult.GeneratedCode);
    }

    [Fact]
    public void Roundtrip_UsingStatementWithGenericType_PreservesGenericType()
    {
        var csharpSource = """
            using System.Collections.Generic;

            public class Test
            {
                public void Run()
                {
                    using (IEnumerator<int> enumerator = new List<int>().GetEnumerator())
                    {
                        enumerator.MoveNext();
                    }
                }
            }
            """;

        // C# -> Calor
        var conversionResult = _converter.Convert(csharpSource);
        Assert.True(conversionResult.Success, GetErrorMessage(conversionResult));
        Assert.NotNull(conversionResult.CalorSource);

        // Calor -> C#
        var compilationResult = Program.Compile(conversionResult.CalorSource!);
        Assert.False(compilationResult.HasErrors,
            $"Roundtrip failed:\n{string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message))}");

        // Verify generic type survives the round-trip
        Assert.Contains("IEnumerator<int>", compilationResult.GeneratedCode);
    }

    #endregion

    #region Bind Statement Format Tests

    [Fact]
    public void Emitter_ImmutableVar_EmitsBindWithoutConst()
    {
        var csharpSource = """
            var count = 42;
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);

        // Should emit §B{name} without :const suffix
        Assert.Contains("§B{count}", result.CalorSource);
        Assert.DoesNotContain(":const", result.CalorSource);
    }

    [Fact]
    public void Emitter_MutableVar_EmitsTildePrefix()
    {
        var csharpSource = """
            int counter = 0;
            counter = counter + 1;
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);

        // Mutable should use ~ prefix
        Assert.Contains("§B{~counter", result.CalorSource);
        Assert.DoesNotContain(":const", result.CalorSource);
    }

    [Fact]
    public void Emitter_TypedImmutableVar_EmitsTypeFirst()
    {
        var csharpSource = """
            int count = 42;
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);

        // Explicit type declaration: type-first format {type:name}
        Assert.Contains("§B{i32:count}", result.CalorSource);
        Assert.DoesNotContain(":const", result.CalorSource);
    }

    [Fact]
    public void Emitter_TypedMutableVar_EmitsTildePrefixWithType()
    {
        var csharpSource = """
            int total = 0;
            total = total + 1;
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);

        // Explicit typed mutable: {~name:type} format
        Assert.Contains("§B{~total:i32}", result.CalorSource);
        Assert.DoesNotContain(":const", result.CalorSource);
    }

    [Fact]
    public void Emitter_EmittedBind_ReParsesWithCorrectSemantics()
    {
        // C# with explicit typed mutable and immutable variables
        var csharpSource = """
            int x = 10;
            int y = 20;
            y = y + x;
            """;

        var result = _converter.Convert(csharpSource);
        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);

        // Re-parse the emitted Calor
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(result.CalorSource!, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var reparsed = parser.Parse();

        Assert.False(diagnostics.HasErrors,
            $"Emitter output should re-parse.\nCalor:\n{result.CalorSource}\nErrors: {string.Join("\n", diagnostics.Select(d => d.Message))}");

        var func = Assert.Single(reparsed.Functions);

        // x is immutable (not reassigned), y is mutable (reassigned)
        var bindX = func.Body.OfType<BindStatementNode>().First(b => b.Name == "x");
        Assert.False(bindX.IsMutable);
        Assert.NotNull(bindX.TypeName);

        var bindY = func.Body.OfType<BindStatementNode>().First(b => b.Name == "y");
        Assert.True(bindY.IsMutable);
        Assert.NotNull(bindY.TypeName);
    }

    #endregion

    #region Array Shorthand Initializer Tests

    [Fact]
    public void Convert_ArrayShorthandInitializer_NoFallbackError()
    {
        // int[] nums = { 1, 2, 3 }; uses InitializerExpressionSyntax, not ArrayCreationExpressionSyntax
        var csharpSource = """
            int[] nums = { 1, 2, 3 };
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        // Should NOT contain §ERR (fallback)
        Assert.DoesNotContain("§ERR", result.CalorSource);
        Assert.Contains("§ARR", result.CalorSource);
    }

    [Fact]
    public void Convert_ArrayShorthandInitializer_Roundtrip()
    {
        var csharpSource = """
            int[] nums = { 1, 2, 3 };
            """;

        var conversionResult = _converter.Convert(csharpSource);
        Assert.True(conversionResult.Success, GetErrorMessage(conversionResult));

        var compilationResult = Program.Compile(conversionResult.CalorSource!);
        Assert.False(compilationResult.HasErrors,
            $"Roundtrip failed:\nCalor: {conversionResult.CalorSource}\n{string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message))}");
        Assert.Contains("new int[]", compilationResult.GeneratedCode);
    }

    [Fact]
    public void Convert_StringArrayShorthandInitializer_NoFallbackError()
    {
        var csharpSource = """
            string[] names = { "Alice", "Bob" };
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.DoesNotContain("§ERR", result.CalorSource);
    }

    [Fact]
    public void Convert_BareArrayInitializer_InfersElementType()
    {
        var csharpSource = """
            double[] values = { 1.0, 2.5, 3.7 };
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("§ARR{", result.CalorSource);
        Assert.DoesNotContain("§ERR", result.CalorSource);
    }

    [Fact]
    public void Convert_BareArrayInitializer_RoundtripsSuccessfully()
    {
        var csharpSource = """
            public class Test
            {
                public void Run()
                {
                    int[] numbers = { 5, 4, 1, 3, 9, 8, 6, 7, 2, 0 };
                }
            }
            """;

        var conversionResult = _converter.Convert(csharpSource);
        Assert.True(conversionResult.Success, GetErrorMessage(conversionResult));
        Assert.DoesNotContain("§ERR", conversionResult.CalorSource);

        var compilationResult = Program.Compile(conversionResult.CalorSource!);
        Assert.False(compilationResult.HasErrors,
            $"Roundtrip failed:\n{string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message))}");
    }

    #endregion

    #region LINQ Method Chain Decomposition Tests

    [Fact]
    public void Convert_LinqChainedCall_DecomposesIntoSeparateStatements()
    {
        var csharpSource = """
            using System.Linq;

            var numbers = new[] { 1, 2, 3, 4, 5 };
            var result = numbers.Where(n => n > 2).First();
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        // Should NOT contain raw C# embedded in Calor (the old buggy behavior)
        Assert.DoesNotContain("§C{(§C{", result.CalorSource);
        // Should contain the chain feature usage
        Assert.Contains("linq-method-chain", result.Context.UsedFeatures);
    }

    [Fact]
    public void Convert_LinqChainedCall_ProducesMultipleBindStatements()
    {
        var csharpSource = """
            using System.Linq;

            var numbers = new[] { 1, 2, 3, 4, 5 };
            var result = numbers.Where(n => n > 2).First();
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);

        var mainFunc = result.Ast.Functions[0];
        // Should have more than 2 statements: numbers decl + chain intermediate + result
        Assert.True(mainFunc.Body.Count >= 3,
            $"Expected at least 3 statements (decomposed chain), got {mainFunc.Body.Count}");
    }

    [Fact]
    public void Convert_LinqChainedCall_IntermediateBindsHaveNoType()
    {
        var csharpSource = """
            using System.Linq;

            var numbers = new[] { 1, 2, 3 };
            var result = numbers.Where(n => n > 1).Select(n => n * 2).ToList();
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);

        var mainFunc = result.Ast.Functions[0];
        // Find intermediate chain binds (they have generated names like _chain001)
        var chainBinds = mainFunc.Body.OfType<BindStatementNode>()
            .Where(b => b.Name.StartsWith("_chain"))
            .ToList();

        Assert.True(chainBinds.Count >= 2, $"Expected at least 2 intermediate chain binds, got {chainBinds.Count}");
        foreach (var bind in chainBinds)
        {
            Assert.Null(bind.TypeName); // Intermediate binds should have no explicit type
        }
    }

    [Fact]
    public void Convert_LinqChainedCall_Roundtrip()
    {
        var csharpSource = """
            using System.Linq;

            var numbers = new[] { 1, 2, 3, 4, 5 };
            var result = numbers.Where(n => n > 2).First();
            """;

        var conversionResult = _converter.Convert(csharpSource);
        Assert.True(conversionResult.Success, GetErrorMessage(conversionResult));

        // Disable effect enforcement since LINQ calls are external
        var compilationResult = Program.Compile(conversionResult.CalorSource!, null,
            new CompilationOptions { EnforceEffects = false });
        Assert.False(compilationResult.HasErrors,
            $"Roundtrip failed:\nCalor: {conversionResult.CalorSource}\n{string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message))}");
    }

    [Fact]
    public void Convert_SingleMethodCall_NotDecomposed()
    {
        // A single method call (not chained) should NOT be decomposed
        var csharpSource = """
            using System.Linq;

            var numbers = new[] { 1, 2, 3 };
            var first = numbers.First();
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);

        var mainFunc = result.Ast.Functions[0];
        // Should not contain chain intermediates
        var chainBinds = mainFunc.Body.OfType<BindStatementNode>()
            .Where(b => b.Name.StartsWith("_chain"))
            .ToList();
        Assert.Empty(chainBinds);
    }

    #endregion

    #region Property Setter Verification Tests

    [Fact]
    public void Convert_ObjectInitializer_NoSetterPrefix()
    {
        // Verify property initializers don't generate §C{set_Prop} patterns
        var csharpSource = """
            var product = new Product { Id = 1, Name = "Widget" };
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        // Should NOT contain set_ prefix for property setters
        Assert.DoesNotContain("set_", result.CalorSource);
    }

    #endregion

    #region Static Class Roundtrip Tests

    [Fact]
    public void Convert_StaticClass_RoundtripProducesStaticKeyword()
    {
        var csharpSource = """
            public static class Utilities
            {
                public static int Add(int a, int b) => a + b;
            }
            """;

        var conversionResult = _converter.Convert(csharpSource);
        Assert.True(conversionResult.Success, GetErrorMessage(conversionResult));

        var compilationResult = Program.Compile(conversionResult.CalorSource!);
        Assert.False(compilationResult.HasErrors,
            $"Roundtrip failed:\nCalor: {conversionResult.CalorSource}\n{string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message))}");

        Assert.Contains("static class Utilities", compilationResult.GeneratedCode);
    }

    #endregion

    #region LINQ Chain Decomposition — Edge Case Tests

    [Fact]
    public void Convert_LinqChainInMethodBody_DecomposesCorrectly()
    {
        // Chain inside a method body (non-top-level), exercises ConvertBlock path
        var csharpSource = """
            public class Service
            {
                public int GetFirst(int[] numbers)
                {
                    var result = numbers.Where(n => n > 0).First();
                    return result;
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        // Should contain decomposed chain, not embedded C#
        Assert.DoesNotContain("§C{(§C{", result.CalorSource);
        Assert.Contains("linq-method-chain", result.Context.UsedFeatures);
    }

    [Fact]
    public void Convert_LinqChainInMethodBody_IntermediateBindsHaveNoType()
    {
        var csharpSource = """
            public class Service
            {
                public void Process(int[] numbers)
                {
                    var result = numbers.Where(n => n > 0).Select(n => n * 2).ToList();
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);

        var cls = result.Ast.Classes[0];
        var method = cls.Methods[0];
        var chainBinds = method.Body.OfType<BindStatementNode>()
            .Where(b => b.Name.StartsWith("_chain"))
            .ToList();

        Assert.True(chainBinds.Count >= 2, $"Expected at least 2 intermediate chain binds, got {chainBinds.Count}");
        foreach (var bind in chainBinds)
        {
            Assert.Null(bind.TypeName); // Intermediate binds should have no explicit type
        }
    }

    [Fact]
    public void Convert_LinqChainInReturnStatement_DecomposesCorrectly()
    {
        // Chain in a return statement exercises DecomposeChainedReturnStatement
        var csharpSource = """
            public class Service
            {
                public int GetFirst(int[] numbers)
                {
                    return numbers.Where(n => n > 0).First();
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.DoesNotContain("§C{(§C{", result.CalorSource);
        Assert.Contains("linq-method-chain", result.Context.UsedFeatures);

        // Should have intermediate bind + return in the method body
        var cls = result.Ast!.Classes[0];
        var method = cls.Methods[0];
        Assert.True(method.Body.Count >= 2,
            $"Expected at least 2 statements (intermediate bind + return), got {method.Body.Count}");

        // Last statement should be a return
        Assert.IsType<ReturnStatementNode>(method.Body[^1]);
        // Previous statements should be intermediate chain binds
        var chainBinds = method.Body.OfType<BindStatementNode>()
            .Where(b => b.Name.StartsWith("_chain"))
            .ToList();
        Assert.NotEmpty(chainBinds);
    }

    [Fact]
    public void Convert_LinqChainInReturnStatement_Roundtrip()
    {
        var csharpSource = """
            public class Service
            {
                public int GetFirst(int[] numbers)
                {
                    return numbers.Where(n => n > 0).First();
                }
            }
            """;

        var conversionResult = _converter.Convert(csharpSource);
        Assert.True(conversionResult.Success, GetErrorMessage(conversionResult));

        var compilationResult = Program.Compile(conversionResult.CalorSource!, null,
            new CompilationOptions { EnforceEffects = false });
        Assert.False(compilationResult.HasErrors,
            $"Roundtrip failed:\nCalor: {conversionResult.CalorSource}\n{string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message))}");
    }

    [Fact]
    public void Convert_TypedLinqChainDeclaration_IntermediatesUntyped()
    {
        // Explicitly typed declaration: intermediate binds should be untyped (var)
        var csharpSource = """
            using System.Collections.Generic;
            using System.Linq;

            IEnumerable<int> result = numbers.Where(n => n > 0).Select(n => n * 2);
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);

        var mainFunc = result.Ast.Functions[0];
        // Intermediate chain binds should have null type
        var chainBinds = mainFunc.Body.OfType<BindStatementNode>()
            .Where(b => b.Name.StartsWith("_chain"))
            .ToList();
        Assert.NotEmpty(chainBinds);
        foreach (var bind in chainBinds)
        {
            Assert.Null(bind.TypeName);
        }

        // Final bind ("result") should have the declared type
        var resultBind = mainFunc.Body.OfType<BindStatementNode>()
            .FirstOrDefault(b => b.Name == "result");
        Assert.NotNull(resultBind);
        Assert.NotNull(resultBind.TypeName);
    }

    [Fact]
    public void Convert_ThreeStepChain_ProducesCorrectIntermediates()
    {
        // Three-step chain: numbers.Where(...).OrderBy(...).ToList()
        var csharpSource = """
            using System.Linq;

            var numbers = new[] { 3, 1, 2 };
            var sorted = numbers.Where(n => n > 0).OrderBy(n => n).ToList();
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);

        var mainFunc = result.Ast.Functions[0];
        // Should have: numbers decl + 2 intermediate binds + final bind = 4 statements
        Assert.True(mainFunc.Body.Count >= 4,
            $"Expected at least 4 statements for 3-step chain, got {mainFunc.Body.Count}");

        var chainBinds = mainFunc.Body.OfType<BindStatementNode>()
            .Where(b => b.Name.StartsWith("_chain"))
            .ToList();
        Assert.Equal(2, chainBinds.Count);
    }

    [Fact]
    public void Convert_StringBuilderChain_PreservesNativeOps()
    {
        // StringBuilder chains should be handled by native sb-* operations, not decomposed
        var csharpSource = """
            using System.Text;

            public class Test
            {
                public string M()
                {
                    return new StringBuilder().Append("a").Append("b").ToString();
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        // Should use native StringBuilder operations (not chain decomposition)
        Assert.Contains("sb-append", result.CalorSource);
        Assert.Contains("sb-tostring", result.CalorSource);
        // Should NOT have chain intermediates
        Assert.DoesNotContain("_chain", result.CalorSource);
    }

    [Fact]
    public void Convert_StringBuilderChainInLocalDecl_PreservesNativeOps()
    {
        // StringBuilder chain in a local declaration should use native ops
        var csharpSource = """
            using System.Text;

            var sb = new StringBuilder();
            var result = sb.Append("hello").ToString();
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.DoesNotContain("_chain", result.CalorSource);
        Assert.DoesNotContain("§C{(§C{", result.CalorSource);
    }

    #endregion

    #region Expression-Level Chain Hoisting Tests

    [Fact]
    public void Convert_LinqChainInIfCondition_HoistsToTempBind()
    {
        var csharpSource = """
            if (numbers.Where(n => n > 0).Any())
            {
                Console.WriteLine("found");
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        // The chain should be hoisted: a temp _chain bind before the if
        Assert.Contains("_chain", result.CalorSource);
        // Should NOT contain the broken CalorEmitter serialization pattern
        Assert.DoesNotContain("§C{(§C{", result.CalorSource);
    }

    [Fact]
    public void Convert_LinqChainAsMethodArgument_HoistsToTempBind()
    {
        var csharpSource = """
            Console.WriteLine(numbers.Where(n => n > 0).Count());
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        // The chain should be hoisted: a temp _chain bind before the call
        Assert.Contains("_chain", result.CalorSource);
        Assert.DoesNotContain("§C{(§C{", result.CalorSource);
    }

    [Fact]
    public void Convert_LinqChainInWhileCondition_HoistsToTempBind()
    {
        var csharpSource = """
            while (items.Where(x => x.Active).Any())
            {
                break;
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("_chain", result.CalorSource);
        Assert.DoesNotContain("§C{(§C{", result.CalorSource);
    }

    [Fact]
    public void Convert_LinqChainInAssignment_HoistsToTempBind()
    {
        var csharpSource = """
            var x = 0;
            x = numbers.Where(n => n > 0).First();
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("_chain", result.CalorSource);
        Assert.DoesNotContain("§C{(§C{", result.CalorSource);
    }

    [Fact]
    public void Convert_ThreeDeepChainInArgument_HoistsAllLevels()
    {
        var csharpSource = """
            Console.WriteLine(numbers.Where(n => n > 0).OrderBy(n => n).First());
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        // A 3-deep chain should produce at least 2 temp binds (_chain for Where, _chain for OrderBy)
        var chainCount = System.Text.RegularExpressions.Regex.Matches(result.CalorSource!, "_chain").Count;
        Assert.True(chainCount >= 2, $"Expected at least 2 _chain temp binds, got {chainCount}. Calor:\n{result.CalorSource}");
        Assert.DoesNotContain("§C{(§C{", result.CalorSource);
    }

    #endregion

    #region Lambda §LAM Emission Tests

    [Fact]
    public void Convert_SingleParamExpressionLambda_EmitsLamBlock()
    {
        var csharpSource = """
            var numbers = new[] { 1, 2, 3, 4, 5 };
            var doubled = numbers.Select(x => x * 2);
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("§LAM{", result.CalorSource);
        Assert.Contains("§/LAM{", result.CalorSource);
        // Should NOT contain arrow notation
        Assert.DoesNotContain("→", result.CalorSource);
    }

    [Fact]
    public void Convert_MultiParamLambda_EmitsLamBlockWithAllParams()
    {
        var csharpSource = """
            public class Test
            {
                public void Run()
                {
                    var list = new List<int> { 3, 1, 2 };
                    list.Sort((a, b) => a - b);
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("§LAM{", result.CalorSource);
        Assert.Contains("§/LAM{", result.CalorSource);
        Assert.DoesNotContain("→", result.CalorSource);
    }

    [Fact]
    public void Convert_LambdaAsLinqArg_ParsesSuccessfully()
    {
        var csharpSource = """
            var numbers = new[] { 1, 2, 3, 4, 5 };
            var evens = numbers.Where(n => n % 2 == 0);
            """;

        var conversionResult = _converter.Convert(csharpSource);
        Assert.True(conversionResult.Success, GetErrorMessage(conversionResult));
        Assert.NotNull(conversionResult.CalorSource);
        Assert.Contains("§LAM{", conversionResult.CalorSource);

        // Verify the emitted Calor re-parses
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(conversionResult.CalorSource!, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        parser.Parse();

        Assert.False(diagnostics.HasErrors,
            $"Lambda Calor output should re-parse.\nCalor:\n{conversionResult.CalorSource}\nErrors: {string.Join("\n", diagnostics.Select(d => d.Message))}");
    }

    [Fact]
    public void Convert_StatementBodyLambda_EmitsLamBlock()
    {
        var csharpSource = """
            public class Test
            {
                public void Run()
                {
                    var list = new List<int> { 1, 2, 3 };
                    list.ForEach(x =>
                    {
                        var doubled = x * 2;
                        Console.WriteLine(doubled);
                    });
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("§LAM{", result.CalorSource);
        Assert.Contains("§/LAM{", result.CalorSource);
        Assert.DoesNotContain("→", result.CalorSource);
    }

    [Fact]
    public void Convert_LambdaParamType_InferredFromSemanticModel()
    {
        var csharpSource = """
            using System.Linq;

            public class Test
            {
                public void Run()
                {
                    var numbers = new int[] { 1, 2, 3 };
                    var evens = numbers.Where(n => n % 2 == 0);
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        // Lambda parameter should be inferred as i32, not object
        Assert.Contains(":i32}", result.CalorSource);
        Assert.DoesNotContain(":object}", result.CalorSource);
    }

    [Fact]
    public void Convert_MultiParamLambdaType_InferredFromSemanticModel()
    {
        var csharpSource = """
            using System.Linq;

            public class Test
            {
                public void Run()
                {
                    var numbers = new int[] { 5, 4, 1, 3, 9 };
                    var result = numbers.TakeWhile((n, index) => n >= index);
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        // Both n and index should be inferred as i32
        Assert.Contains("n:i32", result.CalorSource);
        Assert.Contains("index:i32", result.CalorSource);
        Assert.DoesNotContain(":object", result.CalorSource);
    }

    [Fact]
    public void Convert_LambdaHeavy_FullRoundtrip()
    {
        var csharpSource = """
            public class LinqDemo
            {
                public void Run()
                {
                    var numbers = new int[] { 5, 4, 1, 3, 9, 8, 6, 7, 2, 0 };
                    var evens = numbers.Where(n => n % 2 == 0);
                    var sorted = evens.OrderBy(n => n);
                    var doubled = sorted.Select(n => n * 2);
                }
            }
            """;

        // C# -> Calor
        var conversionResult = _converter.Convert(csharpSource);
        Assert.True(conversionResult.Success, GetErrorMessage(conversionResult));
        Assert.NotNull(conversionResult.CalorSource);
        Assert.Contains("§LAM{", conversionResult.CalorSource);
        Assert.DoesNotContain("§ERR", conversionResult.CalorSource);
        Assert.DoesNotContain("→", conversionResult.CalorSource);

        // Calor -> C#
        var compilationResult = Program.Compile(conversionResult.CalorSource!);
        Assert.False(compilationResult.HasErrors,
            $"Lambda roundtrip failed:\nCalor:\n{conversionResult.CalorSource}\nErrors:\n{string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message))}");
    }

    [Fact]
    public void Convert_AsyncLambda_EmitsLamBlockWithAsync()
    {
        var csharpSource = """
            using System.Threading.Tasks;

            public class Test
            {
                public void Run()
                {
                    Func<int, Task<int>> asyncDoubler = async x => {
                        await Task.Delay(1);
                        return x * 2;
                    };
                }
            }
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("§LAM{", result.CalorSource);
        Assert.Contains("§/LAM{", result.CalorSource);
        Assert.Contains("async", result.CalorSource);
        Assert.DoesNotContain("→", result.CalorSource);
    }

    [Fact]
    public void Convert_StaticMethod_RoundtripsWithStModifier()
    {
        var csharpSource = """
            public static class MathHelper
            {
                public static int Add(int a, int b) => a + b;
                public static int Multiply(int a, int b) => a * b;
            }
            """;

        var conversionResult = _converter.Convert(csharpSource);
        Assert.True(conversionResult.Success, GetErrorMessage(conversionResult));
        Assert.NotNull(conversionResult.CalorSource);

        // Verify :st appears for both class and methods
        var stCount = conversionResult.CalorSource!.Split(":st").Length - 1;
        Assert.True(stCount >= 3, $"Expected at least 3 :st occurrences (1 class + 2 methods), got {stCount}");

        // Full roundtrip
        var compilationResult = Program.Compile(conversionResult.CalorSource!);
        Assert.False(compilationResult.HasErrors,
            $"Static roundtrip failed:\n{string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message))}");
        Assert.Contains("static class", compilationResult.GeneratedCode);
        Assert.Contains("static int Add", compilationResult.GeneratedCode);
    }

    #endregion

    #region Method Modifier Abbreviation Tests

    [Fact]
    public void Parse_MethodWithVrAbbreviation_SetsVirtual()
    {
        var calorSource = """
            §M{m1:TestModule}
              §CL{c001:Base:pub}
                §MT{m001:GetName:pub:vr}
                  §O{str}
                  §R "base"
                §/MT{m001}
              §/CL{c001}
            §/M{m1}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var ast = parser.Parse();

        Assert.False(diagnostics.HasErrors,
            $"Parse errors: {string.Join("\n", diagnostics.Select(d => d.Message))}");
        var method = ast.Classes[0].Methods[0];
        Assert.True(method.IsVirtual, "Method with :vr modifier should have IsVirtual == true");
    }

    [Fact]
    public void Parse_MethodWithOvAbbreviation_SetsOverride()
    {
        var calorSource = """
            §M{m1:TestModule}
              §CL{c001:Derived:pub}
                §EXT{Base}
                §MT{m001:GetName:pub:ov}
                  §O{str}
                  §R "derived"
                §/MT{m001}
              §/CL{c001}
            §/M{m1}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var ast = parser.Parse();

        Assert.False(diagnostics.HasErrors,
            $"Parse errors: {string.Join("\n", diagnostics.Select(d => d.Message))}");
        var method = ast.Classes[0].Methods[0];
        Assert.True(method.IsOverride, "Method with :ov modifier should have IsOverride == true");
    }

    [Fact]
    public void Parse_MethodWithStAbbreviation_SetsStatic()
    {
        var calorSource = """
            §M{m1:TestModule}
              §CL{c001:Utils:pub:st}
                §MT{m001:Add:pub:st}
                  §I{i32:a}
                  §I{i32:b}
                  §O{i32}
                  §R (+ a b)
                §/MT{m001}
              §/CL{c001}
            §/M{m1}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var ast = parser.Parse();

        Assert.False(diagnostics.HasErrors,
            $"Parse errors: {string.Join("\n", diagnostics.Select(d => d.Message))}");
        var method = ast.Classes[0].Methods[0];
        Assert.True(method.IsStatic, "Method with :st modifier should have IsStatic == true");
    }

    [Fact]
    public void Roundtrip_VirtualAndOverride_PreservesModifiers()
    {
        var csharpSource = """
            public class Animal
            {
                public virtual string Speak() { return "..."; }
            }
            """;

        var conversionResult = _converter.Convert(csharpSource);
        Assert.True(conversionResult.Success, GetErrorMessage(conversionResult));
        Assert.NotNull(conversionResult.CalorSource);
        // Emitter uses "virt" abbreviation for virtual
        Assert.Contains(":virt", conversionResult.CalorSource);

        var compilationResult = Program.Compile(conversionResult.CalorSource!);
        Assert.False(compilationResult.HasErrors,
            $"Virtual roundtrip failed:\n{string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message))}");
        Assert.Contains("virtual", compilationResult.GeneratedCode);
    }

    #endregion

    #region Static Class :st Parse Tests

    [Fact]
    public void Parse_ClassWithStAbbreviation_SetsIsStaticTrue()
    {
        var calorSource = """
            §M{m1:TestModule}
              §F{main:Main:pub}
                §O{void}
              §/F{main}
              §CL{c001:Foo:pub:st}
              §/CL{c001}
            §/M{m1}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        var ast = parser.Parse();

        Assert.False(diagnostics.HasErrors,
            $"Parse errors: {string.Join("\n", diagnostics.Select(d => d.Message))}");
        Assert.Single(ast.Classes);
        Assert.True(ast.Classes[0].IsStatic, "Class with :st modifier should have IsStatic == true");
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
