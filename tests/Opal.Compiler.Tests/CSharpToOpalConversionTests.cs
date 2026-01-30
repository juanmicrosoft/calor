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

    #region Object Initializer Tests

    [Fact]
    public void Convert_ObjectInitializer_PreservesPropertyAssignments()
    {
        var csharpSource = """
            var person = new Person { Name = "John", Age = 30, City = "NYC" };
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.OpalSource);
        Assert.Contains("§NEW{Person}", result.OpalSource);
        Assert.Contains("Name:", result.OpalSource);
        Assert.Contains("Age:", result.OpalSource);
        Assert.Contains("City:", result.OpalSource);
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
        Assert.NotNull(result.OpalSource);
        Assert.Contains("§NEW{Settings}", result.OpalSource);
        Assert.Contains("Timeout:", result.OpalSource);
        Assert.Contains("Enabled:", result.OpalSource);
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
        Assert.NotNull(result.OpalSource);
        Assert.Contains("Order", result.OpalSource);
        Assert.Contains("Customer:", result.OpalSource);
        Assert.Contains("Total:", result.OpalSource);
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
        Assert.Contains("partial", result.OpalSource);
        Assert.Contains("static", result.OpalSource);
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
        Assert.NotNull(result.OpalSource);
        Assert.Contains("§SUB", result.OpalSource);
        Assert.Contains("+=", result.OpalSource);
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
        Assert.NotNull(result.OpalSource);
        Assert.Contains("§UNSUB", result.OpalSource);
        Assert.Contains("-=", result.OpalSource);
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
        Assert.NotNull(result.OpalSource);
        Assert.Contains("§SUB", result.OpalSource);
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
        Assert.NotNull(result.OpalSource);
        Assert.Contains("§CONTINUE", result.OpalSource);
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
        Assert.NotNull(result.OpalSource);
        Assert.Contains("§CONTINUE", result.OpalSource);
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
        Assert.NotNull(result.OpalSource);
        Assert.Contains("§CONTINUE", result.OpalSource);
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
        Assert.NotNull(result.OpalSource);
        Assert.Contains("§BREAK", result.OpalSource);
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
        Assert.NotNull(result.OpalSource);
        Assert.Contains("§PROP", result.OpalSource);
        Assert.Contains("= \"Unknown\"", result.OpalSource);
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
        Assert.NotNull(result.OpalSource);
        // Events are emitted as fields since the parser doesn't support §EVT in class bodies
        Assert.Contains("§FLD", result.OpalSource);
        Assert.Contains("SpeakRequest", result.OpalSource);
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
        Assert.NotNull(result.OpalSource);
        Assert.Contains("compound-assignment", result.Context.UsedFeatures);
        // Should emit as §ASSIGN _count (+ _count value)
        Assert.Contains("§ASSIGN", result.OpalSource);
        Assert.Contains("(+", result.OpalSource);
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
        Assert.NotNull(result.OpalSource);
        Assert.Contains("compound-assignment", result.Context.UsedFeatures);
        Assert.Contains("(-", result.OpalSource);
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
        Assert.NotNull(result.OpalSource);
        Assert.Contains("compound-assignment", result.Context.UsedFeatures);
        Assert.Contains("(*", result.OpalSource);
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
        Assert.NotNull(result.OpalSource);
        Assert.Contains("compound-assignment", result.Context.UsedFeatures);
        Assert.Contains("(/", result.OpalSource);
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
        Assert.NotNull(result.OpalSource);
        // Using statements are converted to try/finally for disposal
        Assert.Contains("§TRY{", result.OpalSource);
        Assert.Contains("§FINALLY", result.OpalSource);
        Assert.Contains("Dispose", result.OpalSource);
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
        Assert.NotNull(result.OpalSource);
        // Using statements are converted to try/finally
        Assert.Contains("§TRY{", result.OpalSource);
        Assert.Contains("writer", result.OpalSource);
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
        Assert.NotNull(result.OpalSource);
        // Should have two try/finally blocks (one per using)
        var tryCount = result.OpalSource.Split("§TRY{using_").Length - 1;
        Assert.Equal(2, tryCount);
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
        Assert.NotNull(result.OpalSource);
        // Should NOT contain double dot (?.. or ..)
        Assert.DoesNotContain("?..", result.OpalSource);
        Assert.DoesNotContain("..", result.OpalSource.Replace("...", "")); // Ignore spread operator
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
        Assert.NotNull(result.OpalSource);
        Assert.Contains("?.Length", result.OpalSource);
        Assert.DoesNotContain("?..Length", result.OpalSource);
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
    public void Convert_DoWhileLoop_GeneratesCorrectOpal()
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
        Assert.NotNull(result.OpalSource);
        Assert.Contains("§DO", result.OpalSource);
        Assert.Contains("§/DO", result.OpalSource);
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
