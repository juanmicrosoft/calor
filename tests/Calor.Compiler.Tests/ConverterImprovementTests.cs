using Calor.Compiler.Ast;
using Calor.Compiler.Formatting;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for C#→Calor converter improvements:
/// - §ERR reduction (throw expressions, default, target-typed new, null-conditional methods)
/// - §/NEW closing tag emission
/// - §THIS lowercase in member access
/// </summary>
public class ConverterImprovementTests
{
    private readonly CSharpToCalorConverter _converter = new();

    #region A1: Throw Expressions

    [Fact]
    public void Migration_ThrowExpressionInCoalesce_HoistsNullGuard()
    {
        var csharp = """
            public class Service
            {
                public string Process(string? input)
                {
                    return input ?? throw new Exception("bad");
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);

        // Should hoist an if-null-throw guard before the return
        Assert.Equal(2, method.Body.Count);
        var guard = Assert.IsType<IfStatementNode>(method.Body[0]);
        var ret = Assert.IsType<ReturnStatementNode>(method.Body[1]);

        // Guard condition: (== input null)
        var nullCheck = Assert.IsType<BinaryOperationNode>(guard.Condition);
        Assert.Equal(BinaryOperator.Equal, nullCheck.Operator);

        // Guard body: throw with preserved exception type
        Assert.Single(guard.ThenBody);
        var throwStmt = Assert.IsType<ThrowStatementNode>(guard.ThenBody[0]);
        Assert.IsType<NewExpressionNode>(throwStmt.Exception);

        // Return is just the variable reference (no conditional wrapper)
        Assert.IsType<ReferenceNode>(ret.Expression);
    }

    [Fact]
    public void Migration_CoalesceThrow_Assignment_HoistsNullGuard()
    {
        var csharp = """
            public class Config
            {
                private readonly string _name;
                public Config(string name)
                {
                    _name = name ?? throw new ArgumentNullException(nameof(name));
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var ctor = Assert.Single(cls.Constructors);

        // Should hoist an if-null-throw guard before the assignment
        Assert.Equal(2, ctor.Body.Count);
        var guard = Assert.IsType<IfStatementNode>(ctor.Body[0]);
        Assert.IsType<AssignmentStatementNode>(ctor.Body[1]);

        // Guard body preserves ArgumentNullException type
        var throwStmt = Assert.IsType<ThrowStatementNode>(guard.ThenBody[0]);
        var newExpr = Assert.IsType<NewExpressionNode>(throwStmt.Exception);
        Assert.Contains("ArgumentNullException", newExpr.TypeName);
    }

    [Fact]
    public void Migration_CoalesceThrow_LocalDeclaration_HoistsNullGuard()
    {
        var csharp = """
            public class Service
            {
                public void Run(string? value)
                {
                    var name = value ?? throw new Exception("msg");
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);

        // Should hoist an if-null-throw guard before the binding
        Assert.Equal(2, method.Body.Count);
        Assert.IsType<IfStatementNode>(method.Body[0]);
        Assert.IsType<BindStatementNode>(method.Body[1]);
    }

    [Fact]
    public void Migration_RegularCoalesce_StillConvertsToConditional()
    {
        var csharp = """
            public class Service
            {
                public string Fallback(string? input)
                {
                    return input ?? "default";
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);

        // Regular ?? should still produce a ConditionalExpressionNode (no hoisting)
        Assert.Single(method.Body);
        var ret = Assert.IsType<ReturnStatementNode>(method.Body[0]);
        Assert.IsType<ConditionalExpressionNode>(ret.Expression);
    }

    [Fact]
    public void Migration_CoalesceThrow_CalorOutput_PreservesExceptionType()
    {
        var csharp = """
            public class Service
            {
                public string Name { get; }
                public Service(string name)
                {
                    Name = name ?? throw new ArgumentNullException(nameof(name));
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);

        // Should contain the exception type in a §NEW, not §ERR
        Assert.Contains("ArgumentNullException", result.CalorSource);
        Assert.DoesNotContain("§ERR", result.CalorSource);
    }

    [Fact]
    public void Migration_CoalesceThrow_MethodCall_HoistsToTempVariable()
    {
        var csharp = """
            public class Service
            {
                public string GetName() => "test";
                public string Process()
                {
                    return GetName() ?? throw new InvalidOperationException("no name");
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        // GetName is first method, Process is second
        var method = cls.Methods[1];

        // Should hoist: temp bind, then if-null-throw guard, then return temp ref
        Assert.Equal(3, method.Body.Count);
        Assert.IsType<BindStatementNode>(method.Body[0]);
        Assert.IsType<IfStatementNode>(method.Body[1]);
        var ret = Assert.IsType<ReturnStatementNode>(method.Body[2]);

        // Return should reference the temp variable, not the original call
        var returnRef = Assert.IsType<ReferenceNode>(ret.Expression);
        Assert.StartsWith("_nct", returnRef.Name);
    }

    [Fact]
    public void Migration_NestedCoalesceThrow_ProducesCorrectOutput()
    {
        var csharp = """
            public class Service
            {
                public string Resolve(string? a, string? b)
                {
                    return a ?? b ?? throw new InvalidOperationException("both null");
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);

        // The inner (b ?? throw) is handled first, hoisting a guard for b.
        // The outer (a ?? <inner>) becomes a regular conditional since <inner> returns b.
        // Expect: if-null-throw guard for b, then return with conditional for a.
        Assert.True(method.Body.Count >= 2,
            $"Expected at least 2 statements, got {method.Body.Count}");
        Assert.IsType<IfStatementNode>(method.Body[0]);
    }

    [Fact]
    public void Migration_ThrowExpressionInTernary_HoistsGuard()
    {
        var csharp = """
            public class Service
            {
                public int Check(bool flag)
                {
                    return flag ? 42 : throw new InvalidOperationException("nope");
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);

        // Ternary throw is now hoisted to a guard: if (!flag) throw ...
        Assert.Equal(2, method.Body.Count);
        var guard = Assert.IsType<IfStatementNode>(method.Body[0]);
        var throwStmt = Assert.IsType<ThrowStatementNode>(guard.ThenBody[0]);
        Assert.IsType<NewExpressionNode>(throwStmt.Exception);

        // Return statement has the value directly (no conditional)
        var ret = Assert.IsType<ReturnStatementNode>(method.Body[1]);
        Assert.IsType<IntLiteralNode>(ret.Expression);
    }

    #endregion

    #region A2: Default Expressions

    [Fact]
    public void Migration_DefaultLiteral_ConvertsToDefault()
    {
        var csharp = """
            public class Service
            {
                public int GetValue()
                {
                    return default;
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        var ret = Assert.IsType<ReturnStatementNode>(method.Body[0]);
        var refNode = Assert.IsType<ReferenceNode>(ret.Expression);
        Assert.Equal("default", refNode.Name);
    }

    [Fact]
    public void Migration_DefaultOfInt_ConvertsToZero()
    {
        var csharp = """
            public class Service
            {
                public int GetValue()
                {
                    return default(int);
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        var ret = Assert.IsType<ReturnStatementNode>(method.Body[0]);
        var intLit = Assert.IsType<IntLiteralNode>(ret.Expression);
        Assert.Equal(0, intLit.Value);
    }

    [Fact]
    public void Migration_DefaultOfBool_ConvertsToFalse()
    {
        var csharp = """
            public class Service
            {
                public bool GetValue()
                {
                    return default(bool);
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        var ret = Assert.IsType<ReturnStatementNode>(method.Body[0]);
        var boolLit = Assert.IsType<BoolLiteralNode>(ret.Expression);
        Assert.False(boolLit.Value);
    }

    [Fact]
    public void Migration_DefaultOfString_ConvertsToNull()
    {
        var csharp = """
            public class Service
            {
                public string GetValue()
                {
                    return default(string);
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        var ret = Assert.IsType<ReturnStatementNode>(method.Body[0]);
        var refNode = Assert.IsType<ReferenceNode>(ret.Expression);
        Assert.Equal("null", refNode.Name);
    }

    #endregion

    #region A3: Target-Typed New With Arguments

    [Fact]
    public void Migration_TargetTypedNewWithArgs_ConvertsToNew()
    {
        var csharp = """
            using System.Collections.Generic;
            public class Service
            {
                public List<int> Create()
                {
                    List<int> list = new(16);
                    return list;
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);

        // First statement should be a bind statement with a NewExpressionNode
        var bindStmt = Assert.IsType<BindStatementNode>(method.Body[0]);
        var newExpr = Assert.IsType<NewExpressionNode>(bindStmt.Initializer);
        Assert.Equal("List<i32>", newExpr.TypeName); // Type inferred from declaration
        Assert.Single(newExpr.Arguments);
    }

    #endregion

    #region A4: Null-Conditional Method Calls

    [Fact]
    public void Migration_NullConditionalMethod_ConvertsArgs()
    {
        var csharp = """
            public class Service
            {
                public string? GetName(Service? obj, int x)
                {
                    return obj?.ToString(x);
                }
                public string ToString(int value) { return ""; }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var getNameMethod = cls.Methods[0];

        // Should contain a NullConditionalNode in the return statement
        var ret = Assert.IsType<ReturnStatementNode>(getNameMethod.Body[0]);
        var nullCond = Assert.IsType<NullConditionalNode>(ret.Expression);
        Assert.Contains("ToString(", nullCond.MemberName);
    }

    #endregion

    #region B: §/NEW Closing Tag

    [Fact]
    public void CalorEmitter_NewExpression_EmitsClosingTag()
    {
        var span = new TextSpan(0, 0, 0, 0);
        var newExpr = new NewExpressionNode(span, "List", new List<string> { "int" },
            new List<ExpressionNode>());
        var emitter = new CalorEmitter();
        var output = newExpr.Accept(emitter);

        Assert.Contains("§/NEW", output);
    }

    [Fact]
    public void CalorFormatter_NewExpression_EmitsClosingTag()
    {
        var csharp = """
            public class Service
            {
                public void Run()
                {
                    var list = new System.Collections.Generic.List<int>();
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var formatter = new CalorFormatter();
        var formatted = formatter.Format(result.Ast!);

        // Any §NEW tag should have a corresponding §/NEW
        if (formatted.Contains("§NEW{"))
        {
            Assert.Contains("§/NEW", formatted);
        }
    }

    #endregion

    #region C: §THIS in Member Access

    [Fact]
    public void CalorEmitter_ThisFieldAccess_EmitsLowercase()
    {
        var span = new TextSpan(0, 0, 0, 0);
        var thisExpr = new ThisExpressionNode(span);
        var fieldAccess = new FieldAccessNode(span, thisExpr, "Name");
        var emitter = new CalorEmitter();
        var output = fieldAccess.Accept(emitter);

        Assert.Equal("this.Name", output);
        Assert.DoesNotContain("§THIS", output);
    }

    [Fact]
    public void CalorEmitter_BaseFieldAccess_EmitsLowercase()
    {
        var span = new TextSpan(0, 0, 0, 0);
        var baseExpr = new BaseExpressionNode(span);
        var fieldAccess = new FieldAccessNode(span, baseExpr, "Name");
        var emitter = new CalorEmitter();
        var output = fieldAccess.Accept(emitter);

        Assert.Equal("base.Name", output);
        Assert.DoesNotContain("§BASE", output);
    }

    [Fact]
    public void Migration_ThisMethodCall_EmitsLowercaseThis()
    {
        var csharp = """
            public class Service
            {
                public void Run()
                {
                    this.Process();
                }
                public void Process() { }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CalorEmitter();
        var output = emitter.Emit(result.Ast!);

        // Should contain this.Process, not §THIS.Process
        Assert.Contains("this.Process", output);
        Assert.DoesNotContain("§THIS.Process", output);
    }

    #endregion

    #region Edge Cases: Throw Expression with Existing Variable

    [Fact]
    public void Migration_ThrowExpressionWithVariable_HoistsNullGuard()
    {
        var csharp = """
            public class Service
            {
                public string Process(string? input, Exception ex)
                {
                    return input ?? throw ex;
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);

        // ?? throw ex now hoists a null guard before the return
        Assert.Equal(2, method.Body.Count);
        var guard = Assert.IsType<IfStatementNode>(method.Body[0]);
        var ret = Assert.IsType<ReturnStatementNode>(method.Body[1]);

        // Guard body: throw with the variable reference
        var throwStmt = Assert.IsType<ThrowStatementNode>(guard.ThenBody[0]);
        Assert.IsType<ReferenceNode>(throwStmt.Exception);

        // Return is just the variable reference
        Assert.IsType<ReferenceNode>(ret.Expression);
    }

    #endregion

    #region Edge Cases: Default Expression with Custom Type

    [Fact]
    public void Migration_DefaultOfCustomType_ConvertsToDefaultReference()
    {
        var csharp = """
            public class MyClass { }
            public class Service
            {
                public MyClass GetValue()
                {
                    return default(MyClass);
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var service = result.Ast!.Classes.First(c => c.Name == "Service");
        var method = Assert.Single(service.Methods);
        var ret = Assert.IsType<ReturnStatementNode>(method.Body[0]);
        // Unknown type falls through to "default" reference
        var refNode = Assert.IsType<ReferenceNode>(ret.Expression);
        Assert.Equal("default", refNode.Name);
    }

    #endregion

    #region Edge Cases: Target-Typed New with Initializer

    [Fact]
    public void Migration_TargetTypedNewWithInitializer_ConvertsToNew()
    {
        var csharp = """
            public class Options
            {
                public int Timeout { get; set; }
                public bool Verbose { get; set; }
            }
            public class Service
            {
                public Options Create()
                {
                    Options opts = new(42) { Verbose = true };
                    return opts;
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var service = result.Ast!.Classes.First(c => c.Name == "Service");
        var method = Assert.Single(service.Methods);
        var bindStmt = Assert.IsType<BindStatementNode>(method.Body[0]);
        var newExpr = Assert.IsType<NewExpressionNode>(bindStmt.Initializer);
        Assert.Equal("Options", newExpr.TypeName); // Type inferred from declaration
        Assert.Single(newExpr.Arguments);
        Assert.Single(newExpr.Initializers);
        Assert.Equal("Verbose", newExpr.Initializers[0].PropertyName);
    }

    #endregion

    #region Negative Tests: §ERR Fallback Removal

    [Fact]
    public void Migration_ThrowExpression_DoesNotProduceFallback()
    {
        var csharp = """
            public class Service
            {
                public string Process(string? input)
                {
                    return input ?? throw new Exception("bad");
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        // The AST should not contain any FallbackExpressionNode for throw expressions
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        AssertNoFallbackExpressions(method.Body);
    }

    [Fact]
    public void Migration_DefaultExpressions_DoNotProduceFallback()
    {
        var csharp = """
            public class Service
            {
                public int A() { return default(int); }
                public bool B() { return default(bool); }
                public string C() { return default(string); }
                public int D() { return default; }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        foreach (var method in cls.Methods)
        {
            AssertNoFallbackExpressions(method.Body);
        }
    }

    [Fact]
    public void Migration_TargetTypedNew_DoesNotProduceFallback()
    {
        var csharp = """
            using System.Collections.Generic;
            public class Service
            {
                public List<int> Create()
                {
                    List<int> list = new(16);
                    return list;
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        AssertNoFallbackExpressions(method.Body);
    }

    #endregion

    #region Parser Roundtrip: §/NEW

    [Fact]
    public void Parser_NewExpressionWithClosingTag_ParsesCorrectly()
    {
        var calorSource = """
            §M{m1:Test}
            §CL{c1:Service}
            §MT{m2:Create:pub}
              §O{string}
              §B{string:x} §NEW{StringBuilder} §A "hello" §/NEW
              §R x
            §/MT{m2}
            §/CL{c1}
            §/M{m1}
            """;

        var compilationResult = Program.Compile(calorSource);

        Assert.False(compilationResult.HasErrors,
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void Parser_NewExpressionWithoutArgs_ClosingTagParsesCorrectly()
    {
        var calorSource = """
            §M{m1:Test}
            §CL{c1:Service}
            §MT{m2:Create:pub}
              §O{string}
              §B{List<i32>:items} §NEW{List<i32>} §/NEW
              §R items
            §/MT{m2}
            §/CL{c1}
            §/M{m1}
            """;

        var compilationResult = Program.Compile(calorSource);

        Assert.False(compilationResult.HasErrors,
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void Parser_NewExpressionWithoutClosingTag_StillParses()
    {
        // Backward compatibility: §/NEW is optional
        var calorSource = """
            §M{m1:Test}
            §CL{c1:Service}
            §MT{m2:Create:pub}
              §O{string}
              §B{string:x} §NEW{StringBuilder} §A "hello"
              §R x
            §/MT{m2}
            §/CL{c1}
            §/M{m1}
            """;

        var compilationResult = Program.Compile(calorSource);

        Assert.False(compilationResult.HasErrors,
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));
    }

    #endregion

    #region CalorFormatter: §/NEW in Record Creation

    [Fact]
    public void CalorFormatter_RecordCreation_EmitsClosingTag()
    {
        var csharp = """
            public record Person(string Name, int Age);
            public class Service
            {
                public Person Create()
                {
                    return new Person("Alice", 30);
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var formatter = new CalorFormatter();
        var formatted = formatter.Format(result.Ast!);

        // Record creation should have §/NEW closing tag
        if (formatted.Contains("§NEW{"))
        {
            Assert.Contains("§/NEW", formatted);
        }
    }

    [Fact]
    public void CalorEmitter_NewExpressionZeroArgs_EmitsClosingTag()
    {
        var span = new TextSpan(0, 0, 0, 0);
        var newExpr = new NewExpressionNode(span, "List", new List<string>(),
            new List<ExpressionNode>());
        var emitter = new CalorEmitter();
        var output = newExpr.Accept(emitter);

        Assert.Contains("§NEW{List}", output);
        Assert.Contains("§/NEW", output);
    }

    #endregion

    #region §/NEW Emitter→Parser Roundtrip via C# Conversion

    [Fact]
    public void Roundtrip_NewExpression_EmitsAndParsesClosingTag()
    {
        var csharp = """
            public class MyException : System.Exception
            {
                public MyException(string msg) : base(msg) { }
            }
            public class Service
            {
                public void Run()
                {
                    var ex = new MyException("test error");
                }
            }
            """;

        // C# → Calor (emitter produces §/NEW)
        var conversionResult = _converter.Convert(csharp);
        Assert.True(conversionResult.Success, GetErrorMessage(conversionResult));
        Assert.Contains("§/NEW", conversionResult.CalorSource!);

        // Calor → C# (parser consumes §/NEW)
        var compilationResult = Program.Compile(conversionResult.CalorSource!);
        Assert.False(compilationResult.HasErrors,
            "Roundtrip failed:\n" +
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));
    }

    #endregion

    #region Helpers

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Success) return string.Empty;
        return string.Join("\n", result.Issues.Select(i => i.ToString()));
    }

    /// <summary>
    /// Recursively checks that no FallbackExpressionNode exists in the statement list.
    /// </summary>
    private static void AssertNoFallbackExpressions(IReadOnlyList<StatementNode> statements)
    {
        foreach (var stmt in statements)
        {
            if (stmt is ReturnStatementNode ret)
                AssertExpressionNotFallback(ret.Expression);
            else if (stmt is BindStatementNode bind && bind.Initializer != null)
                AssertExpressionNotFallback(bind.Initializer);
        }
    }

    private static void AssertExpressionNotFallback(ExpressionNode? expr)
    {
        if (expr == null) return;
        Assert.IsNotType<FallbackExpressionNode>(expr);
        // Check nested expressions
        if (expr is BinaryOperationNode bin)
        {
            AssertExpressionNotFallback(bin.Left);
            AssertExpressionNotFallback(bin.Right);
        }
        else if (expr is ConditionalExpressionNode cond)
        {
            AssertExpressionNotFallback(cond.Condition);
            AssertExpressionNotFallback(cond.WhenTrue);
            AssertExpressionNotFallback(cond.WhenFalse);
        }
    }

    #endregion

    #region Element Access: §IDX vs char-at

    [Fact]
    public void Migration_ArrayIndexing_ConvertsToIdx()
    {
        var csharp = """
            public class Service
            {
                public string GetFirst(string[] args)
                {
                    return args[0];
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        var ret = Assert.IsType<ReturnStatementNode>(method.Body[0]);
        Assert.IsType<ArrayAccessNode>(ret.Expression);
    }

    [Fact]
    public void Migration_ListIndexing_ConvertsToIdx()
    {
        var csharp = """
            using System.Collections.Generic;
            public class Service
            {
                public int GetItem(List<int> items, int i)
                {
                    return items[i];
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        var ret = Assert.IsType<ReturnStatementNode>(method.Body[0]);
        Assert.IsType<ArrayAccessNode>(ret.Expression);
    }

    [Fact]
    public void Migration_StringLiteralIndexing_ConvertsToCharAt()
    {
        var csharp = """
            public class Service
            {
                public char GetChar()
                {
                    return "hello"[0];
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        var ret = Assert.IsType<ReturnStatementNode>(method.Body[0]);
        Assert.IsType<CharOperationNode>(ret.Expression);
    }

    #endregion

    #region Loop Bounds Adjustment

    [Fact]
    public void Migration_ForLessThan_AdjustsBoundDown()
    {
        var csharp = """
            public class Service
            {
                public void Run(int n)
                {
                    for (int i = 0; i < n; i++)
                    {
                        System.Console.WriteLine(i);
                    }
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        var loop = Assert.IsType<ForStatementNode>(method.Body[0]);

        // Upper bound should be (- n 1) for exclusive < bound
        var to = Assert.IsType<BinaryOperationNode>(loop.To);
        Assert.Equal(BinaryOperator.Subtract, to.Operator);
        var right = Assert.IsType<IntLiteralNode>(to.Right);
        Assert.Equal(1, right.Value);
    }

    [Fact]
    public void Migration_ForLessThanOrEqual_NoBoundsAdjustment()
    {
        var csharp = """
            public class Service
            {
                public void Run(int n)
                {
                    for (int i = 0; i <= n; i++)
                    {
                        System.Console.WriteLine(i);
                    }
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        var loop = Assert.IsType<ForStatementNode>(method.Body[0]);

        // Upper bound should be n directly (no adjustment for inclusive <=)
        Assert.IsType<ReferenceNode>(loop.To);
    }

    [Fact]
    public void Migration_ForLessThan_CompoundBound_AdjustsCorrectly()
    {
        // i < arr.Length should produce (- arr.Length 1)
        var csharp = """
            public class Service
            {
                public void Run(int[] arr)
                {
                    for (int i = 0; i < arr.Length; i++)
                    {
                        System.Console.WriteLine(arr[i]);
                    }
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        var loop = Assert.IsType<ForStatementNode>(method.Body[0]);

        // Upper bound should be (- arr.Length 1) wrapping the compound expression
        var to = Assert.IsType<BinaryOperationNode>(loop.To);
        Assert.Equal(BinaryOperator.Subtract, to.Operator);
        var right = Assert.IsType<IntLiteralNode>(to.Right);
        Assert.Equal(1, right.Value);
        // Left side should be the arr.Length expression (FieldAccessNode or similar)
        Assert.NotNull(to.Left);
    }

    [Fact]
    public void Migration_ForGreaterThan_AdjustsBoundUp()
    {
        var csharp = """
            public class Service
            {
                public void Run(int n)
                {
                    for (int i = 10; i > n; i--)
                    {
                        System.Console.WriteLine(i);
                    }
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        var loop = Assert.IsType<ForStatementNode>(method.Body[0]);

        // Upper bound should be (+ n 1) for exclusive > bound
        var to = Assert.IsType<BinaryOperationNode>(loop.To);
        Assert.Equal(BinaryOperator.Add, to.Operator);
        var right = Assert.IsType<IntLiteralNode>(to.Right);
        Assert.Equal(1, right.Value);
    }

    #endregion

    #region Mutable Variable Tracking

    [Fact]
    public void Migration_VariableNeverReassigned_EmitsLet()
    {
        var csharp = """
            public class Service
            {
                public int Run()
                {
                    var x = 42;
                    return x;
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        var bind = Assert.IsType<BindStatementNode>(method.Body[0]);
        Assert.False(bind.IsMutable, "Variable 'x' should be §LET (immutable) — it's never reassigned");
    }

    [Fact]
    public void Migration_VariableReassigned_EmitsMut()
    {
        var csharp = """
            public class Service
            {
                public int Run()
                {
                    var x = 0;
                    x = 42;
                    return x;
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        var bind = Assert.IsType<BindStatementNode>(method.Body[0]);
        Assert.True(bind.IsMutable, "Variable 'x' should be §MUT — it's reassigned");
    }

    [Fact]
    public void Migration_VariableIncremented_EmitsMut()
    {
        var csharp = """
            public class Service
            {
                public int Run()
                {
                    var count = 0;
                    count++;
                    return count;
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        var bind = Assert.IsType<BindStatementNode>(method.Body[0]);
        Assert.True(bind.IsMutable, "Variable 'count' should be §MUT — it's incremented");
    }

    #endregion

    #region Const/Readonly Field Detection

    [Fact]
    public void Migration_ConstField_DetectsConstModifier()
    {
        var csharp = """
            public class Config
            {
                public const int MaxRetries = 3;
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var field = Assert.Single(cls.Fields);
        Assert.Equal("MaxRetries", field.Name);
        Assert.True(field.Modifiers.HasFlag(MethodModifiers.Const));
    }

    [Fact]
    public void Migration_ReadonlyField_DetectsReadonlyModifier()
    {
        var csharp = """
            public class Config
            {
                private readonly string _name = "test";
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var field = Assert.Single(cls.Fields);
        Assert.Equal("_name", field.Name);
        Assert.True(field.Modifiers.HasFlag(MethodModifiers.Readonly));
    }

    [Fact]
    public void Migration_StaticReadonlyField_DetectsBothModifiers()
    {
        var csharp = """
            public class Config
            {
                public static readonly int DefaultTimeout = 30;
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var field = Assert.Single(cls.Fields);
        Assert.True(field.Modifiers.HasFlag(MethodModifiers.Static));
        Assert.True(field.Modifiers.HasFlag(MethodModifiers.Readonly));
    }

    [Fact]
    public void CSharpEmitter_ConstField_EmitsConstKeyword()
    {
        var csharp = """
            public class Config
            {
                public const int MaxRetries = 3;
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new Calor.Compiler.CodeGen.CSharpEmitter();
        var output = emitter.Emit(result.Ast!);

        Assert.Contains("public const int MaxRetries = 3;", output);
    }

    [Fact]
    public void CSharpEmitter_ReadonlyField_EmitsReadonlyKeyword()
    {
        var csharp = """
            public class Config
            {
                private readonly string _name = "test";
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new Calor.Compiler.CodeGen.CSharpEmitter();
        var output = emitter.Emit(result.Ast!);

        Assert.Contains("private readonly string _name", output);
    }

    [Fact]
    public void CalorEmitter_ConstField_EmitsConstModifier()
    {
        var csharp = """
            public class Config
            {
                public const int MaxRetries = 3;
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new Calor.Compiler.Migration.CalorEmitter();
        var output = emitter.Emit(result.Ast!);

        Assert.Contains("const", output);
    }

    #endregion

    #region Modifier Abbreviation Tests

    [Fact]
    public void CalorEmitter_StaticClass_EmitsStatAbbreviation()
    {
        var source = """
            §M{m1:TestMod}
            §CL{c1:Helper:pub:stat}
            §/CL{c1}
            §/M{m1}
            """;

        var ast = Parse(source);
        var emitter = new CalorEmitter();
        var output = emitter.Emit(ast);

        Assert.Contains(":stat}", output);
        Assert.DoesNotContain(":static}", output);
    }

    [Fact]
    public void CalorEmitter_SealedClass_EmitsSealAbbreviation()
    {
        var source = """
            §M{m1:TestMod}
            §CL{c1:Final:pub:seal}
            §/CL{c1}
            §/M{m1}
            """;

        var ast = Parse(source);
        var emitter = new CalorEmitter();
        var output = emitter.Emit(ast);

        Assert.Contains(":seal}", output);
        Assert.DoesNotContain(":sealed}", output);
    }

    [Fact]
    public void RoundTrip_StaticClass_PreservesModifier()
    {
        var source = """
            §M{m1:TestMod}
            §CL{c1:Helper:pub:stat}
            §/CL{c1}
            §/M{m1}
            """;

        // Step 1: Parse original
        var ast = Parse(source);
        Assert.Single(ast.Classes);
        Assert.True(ast.Classes[0].IsStatic);

        // Step 2: Emit back to Calor
        var emitter = new CalorEmitter();
        var emitted = emitter.Emit(ast);

        // Step 3: Re-parse the emitted Calor
        var ast2 = Parse(emitted);
        Assert.Single(ast2.Classes);
        Assert.True(ast2.Classes[0].IsStatic);
    }

    [Fact]
    public void CalorEmitter_SealedMethod_EmitsSealAbbreviation()
    {
        // Use C# converter to get a sealed override method in the AST,
        // then verify CalorEmitter emits "seal" not "sealed"
        var converter = new CSharpToCalorConverter();
        var csharp = """
            public class Base
            {
                public virtual int Compute() => 0;
            }
            public class Derived : Base
            {
                public sealed override int Compute() => 42;
            }
            """;

        var result = converter.Convert(csharp);
        Assert.True(result.Success, string.Join("\n", result.Issues.Select(i => i.Message)));

        var emitter = new CalorEmitter();
        var output = emitter.Emit(result.Ast!);

        Assert.Contains("seal", output);
        Assert.DoesNotContain("sealed", output);
    }

    [Fact]
    public void CalorEmitter_StaticField_EmitsStatAbbreviation()
    {
        var source = """
            §M{m1:TestMod}
            §CL{c1:Counter:pub}
              §FLD{i32:Count:pub:stat}
            §/CL{c1}
            §/M{m1}
            """;

        var ast = Parse(source);
        var emitter = new CalorEmitter();
        var output = emitter.Emit(ast);

        Assert.Contains(":stat}", output);
        Assert.DoesNotContain(":static}", output);
    }

    [Fact]
    public void CalorEmitter_StaticProperty_EmitsStatAbbreviation()
    {
        // Parse Calor source with a static property, verify CalorEmitter
        // emits "stat" in the PROP tag (not "static")
        var source = """
            §M{m1:TestMod}
            §CL{c1:Counter:pub}
              §PROP{p1:Count:i32:pub:stat}
                §GET
                §SET
              §/PROP{p1}
            §/CL{c1}
            §/M{m1}
            """;

        var ast = Parse(source);
        var emitter = new CalorEmitter();
        var output = emitter.Emit(ast);

        // Property tag should include stat modifier (compact form may have accessor suffix)
        Assert.Contains(":stat:", output);
        Assert.DoesNotContain(":static:", output);
    }

    [Fact]
    public void RoundTrip_StaticProperty_PreservesModifier()
    {
        var source = """
            §M{m1:TestMod}
            §CL{c1:Counter:pub}
              §PROP{p1:Instance:Counter:pub:stat}
                §GET
                §SET
              §/PROP{p1}
            §/CL{c1}
            §/M{m1}
            """;

        // Parse → emit → reparse
        var ast = Parse(source);
        var emitter = new CalorEmitter();
        var emitted = emitter.Emit(ast);
        var ast2 = Parse(emitted);

        var prop = Assert.Single(ast2.Classes[0].Properties);
        Assert.True(prop.IsStatic);

        // Also verify generated C# has static keyword
        var csharpEmitter = new Calor.Compiler.CodeGen.CSharpEmitter();
        var csharp = csharpEmitter.Emit(ast2);
        Assert.Contains("static Counter Instance", csharp);
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new Calor.Compiler.Diagnostics.DiagnosticBag();
        var lexer = new Calor.Compiler.Parsing.Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Calor.Compiler.Parsing.Parser(tokens, diagnostics);
        return parser.Parse();
    }

    #endregion

    #region Default Parameter Values

    [Fact]
    public void Migration_DefaultStringParameter_EmitsDefaultValue()
    {
        var csharp = """
            public class Service
            {
                public string Greet(string name, string greeting = "Hello")
                {
                    return greeting + " " + name;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CalorEmitter();
        var output = emitter.Emit(result.Ast!);

        Assert.Contains("= \"Hello\"", output);
    }

    [Fact]
    public void Migration_DefaultNumericParameter_EmitsDefaultValue()
    {
        var csharp = """
            public class Service
            {
                public int Add(int a, int b = 0)
                {
                    return a + b;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CalorEmitter();
        var output = emitter.Emit(result.Ast!);

        Assert.Contains("= 0", output);
    }

    [Fact]
    public void Migration_DefaultBoolParameter_EmitsDefaultValue()
    {
        var csharp = """
            public class Service
            {
                public string Process(string input, bool trim = true)
                {
                    return trim ? input.Trim() : input;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CalorEmitter();
        var output = emitter.Emit(result.Ast!);

        Assert.Contains("= true", output);
    }

    [Fact]
    public void Migration_DefaultNullParameter_EmitsDefaultValue()
    {
        var csharp = """
            public class Service
            {
                public string Process(string? sourceFile = null)
                {
                    return sourceFile ?? "default";
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CalorEmitter();
        var output = emitter.Emit(result.Ast!);

        Assert.Contains("= null", output);
    }

    [Fact]
    public void Migration_NoDefaultParameter_EmitsWithoutDefault()
    {
        var csharp = """
            public class Service
            {
                public int Add(int a, int b)
                {
                    return a + b;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CalorEmitter();
        var output = emitter.Emit(result.Ast!);

        // Should not contain any = sign after parameter declarations
        var lines = output.Split('\n');
        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("§I{"))
            {
                Assert.DoesNotContain("=", line);
            }
        }
    }

    [Fact]
    public void Migration_MixedDefaultParameters_EmitsCorrectly()
    {
        var csharp = """
            public class Service
            {
                public string Format(string value, string prefix = "[", string suffix = "]")
                {
                    return prefix + value + suffix;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CalorEmitter();
        var output = emitter.Emit(result.Ast!);

        // First param (value) should have no default
        // Second param (prefix) should have default "["
        // Third param (suffix) should have default "]"
        var lines = output.Split('\n').Where(l => l.TrimStart().StartsWith("§I{")).ToList();
        Assert.Equal(3, lines.Count);
        Assert.DoesNotContain("=", lines[0]);
        Assert.Contains("= \"[\"", lines[1]);
        Assert.Contains("= \"]\"", lines[2]);
    }

    [Fact]
    public void RoundTrip_DefaultParameterValue_PreservesDefault()
    {
        var csharp = """
            public class Service
            {
                public string Greet(string name, string greeting = "Hello")
                {
                    return greeting;
                }
            }
            """;

        // C# → Calor AST
        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        // Calor AST → Calor source (emitter)
        var emitter = new CalorEmitter();
        var calorSource = emitter.Emit(result.Ast!);
        Assert.Contains("= \"Hello\"", calorSource);

        // Calor source → Calor AST (parser)
        var ast2 = Parse(calorSource);
        var cls = Assert.Single(ast2.Classes);
        var method = Assert.Single(cls.Methods);

        // Verify the greeting parameter has a default value
        Assert.Equal(2, method.Parameters.Count);
        Assert.Null(method.Parameters[0].DefaultValue); // name - no default
        Assert.NotNull(method.Parameters[1].DefaultValue); // greeting - has default
        Assert.IsType<StringLiteralNode>(method.Parameters[1].DefaultValue);
        Assert.Equal("Hello", ((StringLiteralNode)method.Parameters[1].DefaultValue!).Value);
    }

    [Fact]
    public void CalorFormatter_DefaultParameter_EmitsDefaultValue()
    {
        var csharp = """
            public class Service
            {
                public string Greet(string name, string greeting = "Hello")
                {
                    return greeting;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var formatter = new CalorFormatter();
        var formatted = formatter.Format(result.Ast!);

        Assert.Contains("= \"Hello\"", formatted);
    }

    [Fact]
    public void Parser_DefaultParameterValue_ParsesDirectly()
    {
        var calorSource = """
            §M{m1:Test}
            §CL{c1:Service:pub}
            §MT{m1:Greet:pub}
              §I{str:name}
              §I{str:greeting} = "Hello"
              §O{str}
              §R greeting
            §/MT{m1}
            §/CL{c1}
            §/M{m1}
            """;

        var ast = Parse(calorSource);
        var cls = Assert.Single(ast.Classes);
        var method = Assert.Single(cls.Methods);

        Assert.Equal(2, method.Parameters.Count);
        Assert.Null(method.Parameters[0].DefaultValue);
        Assert.NotNull(method.Parameters[1].DefaultValue);
        var defaultStr = Assert.IsType<StringLiteralNode>(method.Parameters[1].DefaultValue);
        Assert.Equal("Hello", defaultStr.Value);
    }

    [Fact]
    public void Parser_DefaultNumericParameterValue_ParsesCorrectly()
    {
        var calorSource = """
            §M{m1:Test}
            §CL{c1:Service:pub}
            §MT{m1:Add:pub}
              §I{i32:a}
              §I{i32:b} = 0
              §O{i32}
              §R (+ a b)
            §/MT{m1}
            §/CL{c1}
            §/M{m1}
            """;

        var ast = Parse(calorSource);
        var cls = Assert.Single(ast.Classes);
        var method = Assert.Single(cls.Methods);

        Assert.Equal(2, method.Parameters.Count);
        Assert.Null(method.Parameters[0].DefaultValue);
        Assert.NotNull(method.Parameters[1].DefaultValue);
        var defaultInt = Assert.IsType<IntLiteralNode>(method.Parameters[1].DefaultValue);
        Assert.Equal(0, defaultInt.Value);
    }

    [Fact]
    public void Migration_DefaultParameter_RecordsFeatureUsage()
    {
        var csharp = """
            public class Service
            {
                public string Greet(string name, string greeting = "Hello")
                {
                    return greeting + " " + name;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        Assert.Contains("default-parameter", result.Context.UsedFeatures);
    }

    [Fact]
    public void Migration_NoDefaultParameter_DoesNotRecordFeature()
    {
        var csharp = """
            public class Service
            {
                public int Add(int a, int b)
                {
                    return a + b;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        Assert.DoesNotContain("default-parameter", result.Context.UsedFeatures);
    }

    [Fact]
    public void Migration_DefaultEnumMemberParameter_EmitsFieldAccess()
    {
        var csharp = """
            using System;
            public class Service
            {
                public bool Compare(string a, string b, StringComparison comparison = StringComparison.Ordinal)
                {
                    return string.Equals(a, b, comparison);
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CalorEmitter();
        var output = emitter.Emit(result.Ast!);

        // Should emit the enum member access as a default value
        Assert.Contains("= StringComparison.Ordinal", output);
    }

    [Fact]
    public void Migration_DefaultNegativeParameter_EmitsNegativeValue()
    {
        var csharp = """
            public class Service
            {
                public int Process(int value, int sentinel = -1)
                {
                    return value + sentinel;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CalorEmitter();
        var output = emitter.Emit(result.Ast!);

        // Negative literal emits as unary negate: (- 1)
        Assert.Contains("= (- 1)", output);
    }

    [Fact]
    public void Parser_BackwardCompat_ParameterWithoutDefault_StillParses()
    {
        // Existing Calor files without default values should continue to parse correctly
        var calorSource = """
            §M{m1:Test}
            §CL{c1:Service:pub}
            §MT{m1:Greet:pub}
              §I{str:name}
              §I{str:greeting}
              §O{str}
              §R greeting
            §/MT{m1}
            §/CL{c1}
            §/M{m1}
            """;

        var ast = Parse(calorSource);
        var cls = Assert.Single(ast.Classes);
        var method = Assert.Single(cls.Methods);

        Assert.Equal(2, method.Parameters.Count);
        Assert.Equal("name", method.Parameters[0].Name);
        Assert.Null(method.Parameters[0].DefaultValue);
        Assert.Equal("greeting", method.Parameters[1].Name);
        Assert.Null(method.Parameters[1].DefaultValue);
    }

    [Fact]
    public void RoundTrip_DefaultEnumParameter_PreservesFieldAccess()
    {
        var calorSource = """
            §M{m1:Test}
            §CL{c1:Service:pub}
            §MT{m1:Compare:pub}
              §I{str:a}
              §I{str:b}
              §I{StringComparison:comparison} = StringComparison.Ordinal
              §O{bool}
              §R true
            §/MT{m1}
            §/CL{c1}
            §/M{m1}
            """;

        // Parse → emit → reparse
        var ast = Parse(calorSource);
        var cls = Assert.Single(ast.Classes);
        var method = Assert.Single(cls.Methods);

        // Verify third parameter has default value
        Assert.Equal(3, method.Parameters.Count);
        Assert.NotNull(method.Parameters[2].DefaultValue);

        // Emit back and verify default is preserved
        var emitter = new CalorEmitter();
        var emitted = emitter.Emit(ast);
        Assert.Contains("= StringComparison.Ordinal", emitted);

        // Reparse and verify — parser reads dotted names as ReferenceNode
        var ast2 = Parse(emitted);
        var method2 = Assert.Single(ast2.Classes).Methods[0];
        Assert.NotNull(method2.Parameters[2].DefaultValue);
        var refNode = Assert.IsType<ReferenceNode>(method2.Parameters[2].DefaultValue);
        Assert.Equal("StringComparison.Ordinal", refNode.Name);
    }

    [Fact]
    public void Migration_ConstructorDefaultParameter_EmitsDefaultValue()
    {
        var csharp = """
            public class Config
            {
                private readonly int _timeout;
                public Config(int timeout = 30)
                {
                    _timeout = timeout;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CalorEmitter();
        var output = emitter.Emit(result.Ast!);

        // Constructor parameter should have default value
        var lines = output.Split('\n').Where(l => l.TrimStart().StartsWith("§I{")).ToList();
        Assert.Single(lines);
        Assert.Contains("= 30", lines[0]);
    }

    #endregion

    #region Target-Typed New in Return/Arrow Contexts (InferTargetType Case 3)

    [Fact]
    public void Migration_TargetTypedNew_InLocalFunctionArrow_InfersReturnType()
    {
        var csharp = """
            public class Example
            {
                public string Get()
                {
                    string Local() => new("hello");
                    return Local();
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        // The local function is hoisted to a module-level §F function.
        // The return expression should be a NewExpressionNode with type "str" (not "object").
        var hoistedFunc = result.Ast!.Functions.FirstOrDefault(f => f.Name == "Local");
        Assert.NotNull(hoistedFunc);
        var ret = Assert.IsType<ReturnStatementNode>(hoistedFunc.Body[0]);
        var newExpr = Assert.IsType<NewExpressionNode>(ret.Expression);
        Assert.Equal("str", newExpr.TypeName);
    }

    [Fact]
    public void Migration_TargetTypedNew_InLocalFunctionReturnStatement_InfersReturnType()
    {
        var csharp = """
            using System.Collections.Generic;
            public class Example
            {
                public List<int> Get()
                {
                    List<int> Local()
                    {
                        return new(16);
                    }
                    return Local();
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var hoistedFunc = result.Ast!.Functions.FirstOrDefault(f => f.Name == "Local");
        Assert.NotNull(hoistedFunc);
        var ret = Assert.IsType<ReturnStatementNode>(hoistedFunc.Body[0]);
        var newExpr = Assert.IsType<NewExpressionNode>(ret.Expression);
        Assert.Equal("List<i32>", newExpr.TypeName);
    }

    [Fact]
    public void Migration_TargetTypedNew_InMethodReturnWithArgs_InfersReturnType()
    {
        var csharp = """
            using System.Collections.Generic;
            public class Factory
            {
                public List<string> Create(int capacity)
                {
                    return new(capacity);
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        var ret = Assert.IsType<ReturnStatementNode>(method.Body[0]);
        var newExpr = Assert.IsType<NewExpressionNode>(ret.Expression);
        Assert.Equal("List<str>", newExpr.TypeName);
    }

    [Fact]
    public void Migration_TargetTypedNew_AsyncMethod_UnwrapsTaskType()
    {
        var csharp = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            public class Service
            {
                public async Task<List<int>> GetAsync()
                {
                    return new();
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        var ret = Assert.IsType<ReturnStatementNode>(method.Body[0]);
        var newExpr = Assert.IsType<NewExpressionNode>(ret.Expression);
        Assert.Equal("List<i32>", newExpr.TypeName);
    }

    [Fact]
    public void Migration_TargetTypedNew_AsyncLocalFunction_UnwrapsTaskType()
    {
        var csharp = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            public class Service
            {
                public async Task<List<int>> Get()
                {
                    async Task<List<int>> LocalAsync()
                    {
                        return new();
                    }
                    return await LocalAsync();
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var hoistedFunc = result.Ast!.Functions.FirstOrDefault(f => f.Name == "LocalAsync");
        Assert.NotNull(hoistedFunc);
        var ret = Assert.IsType<ReturnStatementNode>(hoistedFunc.Body[0]);
        var newExpr = Assert.IsType<NewExpressionNode>(ret.Expression);
        Assert.Equal("List<i32>", newExpr.TypeName);
    }

    [Fact]
    public void Migration_TargetTypedNew_InExpressionBodiedProperty_InfersType()
    {
        var csharp = """
            using System.Collections.Generic;
            public class Factory
            {
                public List<string> Items => new();
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var prop = cls.Properties.FirstOrDefault(p => p.Name == "Items");
        Assert.NotNull(prop);
        Assert.NotNull(prop.Getter);
        Assert.NotEmpty(prop.Getter.Body);
        var ret = Assert.IsType<ReturnStatementNode>(prop.Getter.Body[0]);
        var newExpr = Assert.IsType<NewExpressionNode>(ret.Expression);
        Assert.Equal("List<str>", newExpr.TypeName);
    }

    #endregion

    #region Target-Typed New in Throw / Parameter / Cast Contexts

    [Fact]
    public void Migration_TargetTypedNew_InThrowStatement_InfersException()
    {
        var csharp = """
            using System;
            public class Service
            {
                public void Validate(string? input)
                {
                    if (input == null) throw new("input is null");
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var emitted = new CalorFormatter().Format(result.Ast!);
        Assert.DoesNotContain("NEW{object}", emitted);
        Assert.Contains("NEW{Exception}", emitted);
    }

    [Fact]
    public void Migration_TargetTypedNew_InThrowExpression_InfersException()
    {
        var csharp = """
            using System;
            public class Service
            {
                public string Process(string? input)
                {
                    var result = input ?? throw new("bad");
                    return result;
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var emitted = new CalorFormatter().Format(result.Ast!);
        Assert.DoesNotContain("NEW{object}", emitted);
        Assert.Contains("NEW{Exception}", emitted);
    }

    [Fact]
    public void Migration_TargetTypedNew_InCastExpression_InfersCastType()
    {
        var csharp = """
            using System;
            public class Service
            {
                public Exception GetError()
                {
                    return (ArgumentException)new("bad");
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var emitted = new CalorFormatter().Format(result.Ast!);
        Assert.DoesNotContain("NEW{object}", emitted);
        Assert.Contains("NEW{ArgumentException}", emitted);
    }

    [Fact]
    public void Migration_TargetTypedNew_VariableDecl_InfersType()
    {
        var csharp = """
            using System.Collections.Generic;
            public class Foo
            {
                private List<int> _items = new();
                public Dictionary<string, int> GetMap() => new();
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var emitted = new CalorFormatter().Format(result.Ast!);
        Assert.DoesNotContain("NEW{object}", emitted);
    }

    #endregion
}
