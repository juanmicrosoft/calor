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
    public void Migration_ThrowExpression_ConvertsToErr()
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
        var ret = Assert.IsType<ReturnStatementNode>(method.Body[0]);

        // The null-coalescing (??) is converted as ConditionalExpressionNode:
        // (if (== input null) <throw-expr> input)
        var conditional = Assert.IsType<ConditionalExpressionNode>(ret.Expression);
        // WhenTrue is the throw expression (ERR), WhenFalse is the input variable
        Assert.IsType<ErrExpressionNode>(conditional.WhenTrue);
    }

    [Fact]
    public void Migration_ThrowExpressionInTernary_ConvertsToErr()
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
        var ret = Assert.IsType<ReturnStatementNode>(method.Body[0]);
        var conditional = Assert.IsType<ConditionalExpressionNode>(ret.Expression);
        Assert.IsType<ErrExpressionNode>(conditional.WhenFalse);
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
    public void Migration_ThrowExpressionWithVariable_ConvertsToErr()
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
        var ret = Assert.IsType<ReturnStatementNode>(method.Body[0]);

        // The null-coalescing (??) is now a ConditionalExpressionNode:
        // (if (== input null) <throw-expr> input)
        var conditional = Assert.IsType<ConditionalExpressionNode>(ret.Expression);
        // throw ex → ErrExpressionNode wrapping a ReferenceNode
        var err = Assert.IsType<ErrExpressionNode>(conditional.WhenTrue);
        Assert.IsType<ReferenceNode>(err.Error);
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

        // Property tag should include stat modifier
        Assert.Contains(":stat}", output);
        Assert.DoesNotContain(":static}", output);
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
}
