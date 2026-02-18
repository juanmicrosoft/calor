using Calor.Compiler.Ast;
using Calor.Compiler.Migration;
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
        Assert.Contains("Name:", result.CalorSource);
        Assert.Contains("Age:", result.CalorSource);
        Assert.Contains("City:", result.CalorSource);
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
        Assert.Contains("Timeout:", result.CalorSource);
        Assert.Contains("Enabled:", result.CalorSource);
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
        Assert.Contains("Customer:", result.CalorSource);
        Assert.Contains("Total:", result.CalorSource);
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
        Assert.Contains("static", result.CalorSource);
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

    #region Helper Methods

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Success) return string.Empty;
        return string.Join("\n", result.Issues.Select(i => $"{i.Severity}: {i.Message}"));
    }

    #endregion
}
