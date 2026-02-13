using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class ControlFlowTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    [Fact]
    public void Parse_ForLoop_ReturnsForStatementNode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §L{for1:i:0:10:1}
                §C{Console.WriteLine}
                  §A i
                §/C
              §/L{for1}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var func = module.Functions[0];
        Assert.Single(func.Body);

        var forStmt = func.Body[0] as ForStatementNode;
        Assert.NotNull(forStmt);
        Assert.Equal("i", forStmt.VariableName);
        Assert.Single(forStmt.Body);
    }

    [Fact]
    public void Parse_ForLoop_MismatchedId_ReportsError()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §L{for1:i:0:10}
              §/L{for999}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.MismatchedId &&
            d.Message.Contains("for1") &&
            d.Message.Contains("for999"));
    }

    [Fact]
    public void Parse_IfStatement_ReturnsIfStatementNode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §IF{if1} (== x INT:0)
                §C{Console.WriteLine}
                  §A "Zero"
                §/C
              §/I{if1}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var func = module.Functions[0];
        Assert.Single(func.Body);

        var ifStmt = func.Body[0] as IfStatementNode;
        Assert.NotNull(ifStmt);
        Assert.Single(ifStmt.ThenBody);
        Assert.Empty(ifStmt.ElseIfClauses);
        Assert.Null(ifStmt.ElseBody);
    }

    [Fact]
    public void Parse_IfElseStatement_ReturnsIfStatementNodeWithElse()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §IF{if1} BOOL:true
                §C{Console.WriteLine}
                  §A "Then"
                §/C
              §EL
                §C{Console.WriteLine}
                  §A "Else"
                §/C
              §/I{if1}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var ifStmt = module.Functions[0].Body[0] as IfStatementNode;
        Assert.NotNull(ifStmt);
        Assert.Single(ifStmt.ThenBody);
        Assert.NotNull(ifStmt.ElseBody);
        Assert.Single(ifStmt.ElseBody);
    }

    [Fact]
    public void Parse_IfElseIfElse_ReturnsCorrectStructure()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §IF{if1} (== x INT:1)
                §C{Console.WriteLine}
                  §A "One"
                §/C
              §EI (== x INT:2)
                §C{Console.WriteLine}
                  §A "Two"
                §/C
              §EL
                §C{Console.WriteLine}
                  §A "Other"
                §/C
              §/I{if1}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var ifStmt = module.Functions[0].Body[0] as IfStatementNode;
        Assert.NotNull(ifStmt);
        Assert.Single(ifStmt.ThenBody);
        Assert.Single(ifStmt.ElseIfClauses);
        Assert.NotNull(ifStmt.ElseBody);
    }

    [Fact]
    public void Parse_WhileLoop_ReturnsWhileStatementNode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §WH{w1} (< x INT:10)
                §C{Console.WriteLine}
                  §A x
                §/C
              §/WH{w1}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var whileStmt = module.Functions[0].Body[0] as WhileStatementNode;
        Assert.NotNull(whileStmt);
        Assert.Single(whileStmt.Body);
    }

    [Fact]
    public void Parse_BindStatement_ReturnsBindStatementNode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
            §O{void}
            §B{x:i32} 42
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var bindStmt = module.Functions[0].Body[0] as BindStatementNode;
        Assert.NotNull(bindStmt);
        Assert.Equal("x", bindStmt.Name);
        Assert.Equal("INT", bindStmt.TypeName);  // i32 expands to INT internally
        Assert.NotNull(bindStmt.Initializer);
    }

    [Fact]
    public void Parse_BinaryOperation_ReturnsBinaryOperationNode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Add:pub}
              §I{i32:a}
              §I{i32:b}
              §O{i32}
              §R (+ a b)
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var retStmt = module.Functions[0].Body[0] as ReturnStatementNode;
        Assert.NotNull(retStmt);

        var binOp = retStmt.Expression as BinaryOperationNode;
        Assert.NotNull(binOp);
        Assert.Equal(BinaryOperator.Add, binOp.Operator);
    }

    [Fact]
    public void Compile_ForLoop_GeneratesValidCSharp()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §E{cw}
              §L{l1:i:1:10:1}
                §C{Console.WriteLine}
                  §A i
                §/C
              §/L{l1}
            §/F{f001}
            §/M{m001}
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors);
        Assert.Contains("for (var i = 1; i <= 10; i++)", result.GeneratedCode);
        Assert.Contains("Console.WriteLine(i)", result.GeneratedCode);
    }

    [Fact]
    public void Compile_IfElse_GeneratesValidCSharp()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §E{cw}
              §IF{i1} BOOL:true
                §C{Console.WriteLine}
                  §A STR:"Yes"
                §/C
              §EL
                §C{Console.WriteLine}
                  §A STR:"No"
                §/C
              §/I{i1}
            §/F{f001}
            §/M{m001}
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors);
        Assert.Contains("if (true)", result.GeneratedCode);
        Assert.Contains("else", result.GeneratedCode);
    }

    [Fact]
    public void Compile_Bind_GeneratesValidCSharp()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §E{cw}
              §B{x} INT:42
              §C{Console.WriteLine}
                §A x
              §/C
            §/F{f001}
            §/M{m001}
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors);
        Assert.Contains("var x = 42;", result.GeneratedCode);
        Assert.Contains("Console.WriteLine(x)", result.GeneratedCode);
    }

    [Fact]
    public void Compile_BinaryOperation_GeneratesValidCSharp()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Add:pub}
              §I{i32:a}
              §I{i32:b}
              §O{i32}
              §R (+ a b)
            §/F{f001}
            §/M{m001}
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors);
        Assert.Contains("return (a + b);", result.GeneratedCode);
    }

    [Fact]
    public void Compile_Modulo_GeneratesValidCSharp()
    {
        var source = """
            §M{m001:Test}
            §F{f001:IsEven:pub}
              §I{i32:n}
              §O{bool}
              §R (== (% n INT:2) INT:0)
            §/F{f001}
            §/M{m001}
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors);
        Assert.Contains("return ((n % 2) == 0);", result.GeneratedCode);
    }

    [Fact]
    public void Parse_DoWhileLoop_ReturnsDoWhileStatementNode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §DO{do1}
                §C{Console.WriteLine}
                  §A x
                §/C
              §/DO{do1} (< x INT:10)
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var doStmt = module.Functions[0].Body[0] as DoWhileStatementNode;
        Assert.NotNull(doStmt);
        Assert.Equal("do1", doStmt.Id);
        Assert.Single(doStmt.Body);
        Assert.NotNull(doStmt.Condition);
    }

    [Fact]
    public void Parse_DoWhileLoop_MismatchedId_ReportsError()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §DO{do1}
              §/DO{do999} (< x INT:10)
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.MismatchedId &&
            d.Message.Contains("do1") &&
            d.Message.Contains("do999"));
    }

    [Fact]
    public void Compile_DoWhileLoop_GeneratesValidCSharp()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §E{cw}
              §B{i} INT:0
              §DO{d1}
                §C{Console.WriteLine}
                  §A i
                §/C
              §/DO{d1} (< i INT:10)
            §/F{f001}
            §/M{m001}
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors);
        Assert.Contains("do", result.GeneratedCode);
        Assert.Contains("while ((i < 10));", result.GeneratedCode);
    }

    [Fact]
    public void BindInsideLoop_EmitsAssignmentNotRedeclaration()
    {
        // Test that mutable §B{~name} inside loop emits assignment on subsequent iterations
        // The ~ prefix indicates mutability, enabling reassignment instead of shadowing
        var source = """
            §M{m001:Test}
            §F{f001:SumRange:pub}
              §I{i32:n}
              §O{i32}
              §B{~sum:i32} INT:0
              §L{l1:i:1:n:1}
                §B{~sum:i32} (+ sum i)
              §/L{l1}
              §R sum
            §/F{f001}
            §/M{m001}
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message)));
        // First declaration should be "int sum = 0;"
        Assert.Contains("int sum = 0;", result.GeneratedCode);
        // Inside loop should be assignment "sum = (sum + i);" NOT "int sum = (sum + i);"
        // Check that there's only one "int sum" declaration
        var intSumCount = result.GeneratedCode.Split("int sum").Length - 1;
        Assert.Equal(1, intSumCount);
        // Verify the reassignment exists
        Assert.Contains("sum = (sum + i);", result.GeneratedCode);
    }

    [Fact]
    public void ForLoopWithSExpressionBound_ParsesAndCompiles()
    {
        // Test §L{for1:i:0:(- n 1):1} compiles correctly
        // Note: S-expressions in attributes cannot use INT:1 syntax because ':'
        // is the attribute separator. Use plain literals instead.
        var source = """
            §M{m001:Test}
            §F{f001:ProcessArray:pub}
              §I{i32:n}
              §O{void}
              §E{cw}
              §L{l1:i:0:(- n 1):1}
                §C{Console.WriteLine}
                  §A i
                §/C
              §/L{l1}
            §/F{f001}
            §/M{m001}
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message)));
        // Should generate for loop with (n - 1) as the upper bound
        Assert.Contains("for (var i = 0; i <= (n - 1); i++)", result.GeneratedCode);
    }

    [Fact]
    public void ForLoopWithSExpressionInFromAndStep_ParsesAndCompiles()
    {
        // Test S-expressions work in all three positions: from, to, and step
        // Note: S-expressions in attributes use plain integer literals (not INT:1 syntax)
        // because ':' is the attribute separator.
        var source = """
            §M{m001:Test}
            §F{f001:IterateWithExpressions:pub}
              §I{i32:start}
              §I{i32:end}
              §I{i32:stepSize}
              §O{void}
              §E{cw}
              §L{l1:i:(+ start 1):(- end 1):(* stepSize 2)}
                §C{Console.WriteLine}
                  §A i
                §/C
              §/L{l1}
            §/F{f001}
            §/M{m001}
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message)));
        // Verify expressions are parsed for from, to, and step
        Assert.Contains("(start + 1)", result.GeneratedCode);
        Assert.Contains("(end - 1)", result.GeneratedCode);
        Assert.Contains("(stepSize * 2)", result.GeneratedCode);
    }

    [Fact]
    public void ForLoopWithNestedSExpressions_ParsesAndCompiles()
    {
        // Test nested S-expressions: (+ (- a 1) (- b 2))
        var source = """
            §M{m001:Test}
            §F{f001:NestedExpr:pub}
              §I{i32:a}
              §I{i32:b}
              §O{void}
              §E{cw}
              §L{l1:i:0:(+ (- a 1) (- b 2)):1}
                §C{Console.WriteLine}
                  §A i
                §/C
              §/L{l1}
            §/F{f001}
            §/M{m001}
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message)));
        // Verify nested expressions are parsed correctly
        Assert.Contains("((a - 1) + (b - 2))", result.GeneratedCode);
    }

    [Fact]
    public void ForLoopWithComparisonInCondition_ParsesAndCompiles()
    {
        // Test comparison operators in S-expressions
        var source = """
            §M{m001:Test}
            §F{f001:CompareTest:pub}
              §I{i32:limit}
              §O{void}
              §E{cw}
              §WH{w1} (<= i limit)
                §C{Console.WriteLine}
                  §A i
                §/C
              §/WH{w1}
            §/F{f001}
            §/M{m001}
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message)));
        Assert.Contains("while ((i <= limit))", result.GeneratedCode);
    }

    [Fact]
    public void LogicalOperatorsInCondition_ParsesAndCompiles()
    {
        // Test logical operators (&&, ||) in S-expressions
        var source = """
            §M{m001:Test}
            §F{f001:LogicalTest:pub}
              §I{i32:a}
              §I{i32:b}
              §O{bool}
              §R (&& (> a 0) (< b 100))
            §/F{f001}
            §/M{m001}
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message)));
        Assert.Contains("((a > 0) && (b < 100))", result.GeneratedCode);
    }

    [Fact]
    public void MutableVariableReassignmentInNestedIf_EmitsCorrectly()
    {
        // Test that mutable variable reassignment works correctly in nested if blocks
        // The ~ prefix indicates mutability, enabling reassignment instead of shadowing
        var source = """
            §M{m001:Test}
            §F{f001:NestedIfTest:pub}
              §I{i32:x}
              §O{i32}
              §B{~result:i32} INT:0
              §IF{if1} (> x INT:0)
                §B{~result:i32} INT:1
                §IF{if2} (> x INT:10)
                  §B{~result:i32} INT:2
                §/I{if2}
              §/I{if1}
              §R result
            §/F{f001}
            §/M{m001}
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message)));
        // Should have only ONE declaration of result
        var declarationCount = result.GeneratedCode.Split("int result").Length - 1;
        Assert.Equal(1, declarationCount);
        // Should have multiple assignments
        Assert.Contains("result = 1;", result.GeneratedCode);
        Assert.Contains("result = 2;", result.GeneratedCode);
    }

    [Fact]
    public void ClassMethodsHaveIndependentVariableScopes()
    {
        // Test that variables declared in one method don't affect another
        var source = """
            §M{m001:Test}
            §CL{c001:TestClass:pub}
              §MT{mt001:MethodA:pub}
                §O{void}
                §B{x:i32} INT:1
              §/MT{mt001}
              §MT{mt002:MethodB:pub}
                §O{void}
                §B{x:i32} INT:2
              §/MT{mt002}
            §/CL{c001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var parser = new Parser(lexer.TokenizeAll(), diagnostics);
        var ast = parser.Parse();

        Assert.Empty(diagnostics.Errors);
        var classNode = Assert.Single(ast.Classes);
        Assert.Equal(2, classNode.Methods.Count);

        // Emit code for the class
        var emitter = new CSharpEmitter();
        var code = emitter.Emit(ast);

        // Both methods should have their own "int x" declaration
        var declarationCount = code.Split("int x =").Length - 1;
        Assert.Equal(2, declarationCount);
    }

    [Fact]
    public void ForLoopWithFloatBounds_ParsesAndCompiles()
    {
        // Test that float literals work in S-expressions
        var source = """
            §M{m001:Test}
            §F{f001:FloatTest:pub}
              §I{f64:start}
              §O{void}
              §E{cw}
              §L{l1:i:0:(- start 0.5):1}
                §C{Console.WriteLine}
                  §A i
                §/C
              §/L{l1}
            §/F{f001}
            §/M{m001}
            """;

        var result = Program.Compile(source);

        // This may have type issues due to mixing int loop var with float bound,
        // but it should at least parse the S-expression correctly
        Assert.Contains("(start - 0.5)", result.GeneratedCode);
    }

    [Fact]
    public void BindInIfBlock_ShouldShadowOuterVariable()
    {
        // According to Calor semantics S5-S6: "Inner scope does NOT mutate outer"
        // A bind in an if block should create a new variable that shadows the outer
        var source = """
            §M{m001:Test}
            §F{f001:testShadow:pub}
              §I{bool:cond}
              §O{i32}
              §B{x:i32} INT:10
              §IF{if1} cond
                §B{x:i32} INT:20
              §/I{if1}
              §R x
            §/F{f001}
            §/M{m001}
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var parser = new Parser(lexer.TokenizeAll(), diagnostics);
        var ast = parser.Parse();

        Assert.Empty(diagnostics.Errors);

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(ast);

        // Both should have their own "int x" declaration for proper shadowing
        // If we only have one declaration, then inner x is reassigning outer x (wrong!)
        var declarationCount = code.Split("int x =").Length - 1;

        // EXPECTED: 2 declarations (one outer, one inner for shadowing)
        // CURRENT BEHAVIOR: Check if shadowing is implemented
        // This test documents the expected behavior per Calor semantics
        Assert.Equal(2, declarationCount);
    }
}
