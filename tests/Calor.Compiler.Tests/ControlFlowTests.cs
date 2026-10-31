using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class ControlFlowTests
{
    [Theory]
    [InlineData("§IF true → §R 1")]
    [InlineData("§IF true\n      §R 1\n    §EI false → §R 2")]
    [InlineData("§IF true\n      §R 1\n    §EL → §R 2")]
    public void Parse_ArrowConditionalStatements_AreRejected(string statement)
    {
        var source = "§M{Test}\n  §F{Probe:pub} () -> i32\n    " + statement;
        Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Message.Contains("Arrow conditional statements are removed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("§W value\n      §K _ → 1", true)]
    [InlineData("§W value\n      §K _\n        §R 1", false)]
    [InlineData("§R §W value\n      §K _ → 1", false)]
    public void Parse_StatementMatchArmsRequireBlocks(string statement, bool hasErrors)
    {
        var source = "§M{Test}\n  §F{Probe:pub} (i32:value) -> i32\n    " + statement;
        var module = Parse(source, out var diagnostics);
        Assert.Equal(hasErrors, diagnostics.HasErrors);
        if (hasErrors)
            Assert.Contains(diagnostics, diagnostic =>
                diagnostic.Message.Contains("Statement match arms require", StringComparison.Ordinal));
        else
        {
            var emitted = new Calor.Compiler.Migration.CalorEmitter().Emit(module);
            Parse(emitted, out var emittedDiagnostics);
            Assert.False(emittedDiagnostics.HasErrors,
                string.Join("; ", emittedDiagnostics.Select(diagnostic => diagnostic.Message)));
        }
    }

    [Fact]
    public void Parse_ReturnMatchShortcut_IsRejectedWithExplicitReturnReplacement()
    {
        var source = """
            §M{Test}
              §F{Probe:pub} (i32:value) -> i32
                §W{expr} value
                  §K _
                    §R 1
            """;

        var module = Parse(source, out var diagnostics);
        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Message.Contains("Use §R §W", StringComparison.Ordinal));
        Assert.IsType<MatchStatementNode>(Assert.Single(Assert.Single(module.Functions).Body));
    }

    [Fact]
    public void Parse_ExplicitReturnedMatch_IsAnExpression()
    {
        var source = """
            §M{Test}
              §F{Probe:pub} (i32:value) -> i32
                §R §W value
                  §K _ → 1
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Select(d => d.Message)));
        var returned = Assert.IsType<ReturnStatementNode>(Assert.Single(Assert.Single(module.Functions).Body));
        Assert.IsType<MatchExpressionNode>(returned.Expression);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("→ ", false)]
    public void Parse_LambdaMatchArm_RequiresArrow(string arrow, bool hasErrors)
    {
        var source = $$"""
            §M{Test}
              §F{Probe:pub} (i32:value) -> object
                §R §W value
                  §K _ {{arrow}}§LAM{x:i32} x §/LAM
            """;

        Parse(source, out var diagnostics);
        Assert.Equal(hasErrors, diagnostics.HasErrors);
        if (hasErrors)
            Assert.Contains(diagnostics, diagnostic =>
                diagnostic.Message.Contains("requires →", StringComparison.Ordinal));
    }

    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    [Fact]
    public void Parse_ForLoop_ReturnsForStatementNode()
    {
        var source = """
            §M{Test}
              §F{Main:pub}
                  §O{void}
                  §L{i:0:10:1}
                      §C{Console.WriteLine}
                        §A i
                      §/C
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
    public void Parse_ForLoop_LegacyCloser_ReportsError()
    {
        // §/L is removed closer-form (Phase 4d): rejected with Calor0830, not MismatchedId.
        var source = """
            §M{Test}
            §F{Main:pub}
              §L{i:0:10}
              §/L
            §/F
            §/M
            """;

        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.LegacyCloserForm);
    }

    [Fact]
    public void Parse_IfStatement_ReturnsIfStatementNode()
    {
        var source = """
            §M{Test}
              §F{Main:pub}
                  §O{void}
                  §IF (== x INT:0)
                      §C{Console.WriteLine}
                        §A "Zero"
                      §/C
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
            §M{Test}
              §F{Main:pub}
                  §O{void}
                  §IF BOOL:true
                      §C{Console.WriteLine}
                        §A "Then"
                      §/C
                  §EL
                      §C{Console.WriteLine}
                        §A "Else"
                      §/C
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
            §M{Test}
              §F{Main:pub}
                  §O{void}
                  §IF (== x INT:1)
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
            §M{Test}
              §F{Main:pub}
                  §O{void}
                  §WH (< x INT:10)
                      §C{Console.WriteLine}
                        §A x
                      §/C
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
            §M{Test}
              §F{Main:pub}
                §O{void}
                §B{x:i32} 42
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
            §M{Test}
              §F{Add:pub}
                  §I{i32:a}
                  §I{i32:b}
                  §O{i32}
                  §R (+ a b)
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
            §M{Test}
              §F{Main:pub}
                  §O{void}
                  §E{cw}
                  §L{i:1:10:1}
                      §C{Console.WriteLine}
                        §A i
                      §/C
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors);
        Assert.Contains("var __calorForFrom = 1;", result.GeneratedCode);
        Assert.Contains("var __calorForTo = 10;", result.GeneratedCode);
        Assert.Contains("var __calorForStep = 1;", result.GeneratedCode);
        Assert.Contains("while (true)", result.GeneratedCode);
        Assert.Contains("checked", result.GeneratedCode);
        Assert.Contains("catch (OverflowException)", result.GeneratedCode);
        Assert.Contains("Console.WriteLine(i)", result.GeneratedCode);
    }

    [Fact]
    public void Compile_IfElse_GeneratesValidCSharp()
    {
        var source = """
            §M{Test}
              §F{Main:pub}
                  §O{void}
                  §E{cw}
                  §IF BOOL:true
                      §C{Console.WriteLine}
                        §A STR:"Yes"
                      §/C
                  §EL
                      §C{Console.WriteLine}
                        §A STR:"No"
                      §/C
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
            §M{Test}
              §F{Main:pub}
                  §O{void}
                  §E{cw}
                  §B{x} INT:42
                  §C{Console.WriteLine}
                    §A x
                  §/C
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
            §M{Test}
              §F{Add:pub}
                  §I{i32:a}
                  §I{i32:b}
                  §O{i32}
                  §R (+ a b)
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors);
        Assert.Contains("return checked(a + b);", result.GeneratedCode);
    }

    [Fact]
    public void Compile_Modulo_GeneratesValidCSharp()
    {
        var source = """
            §M{Test}
              §F{IsEven:pub}
                  §I{i32:n}
                  §O{bool}
                  §R (== (% n INT:2) INT:0)
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors);
        Assert.Contains("return n % 2 == 0;", result.GeneratedCode);
    }

    [Fact]
    public void Parse_DoWhileLoop_ReturnsDoWhileStatementNode()
    {
        var source = """
            §M{Test}
              §F{Main:pub}
                  §O{void}
                  §DO
                    §C{Console.WriteLine}
                      §A x
                    §/C
                  §/DO (< x INT:10)
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var doStmt = module.Functions[0].Body[0] as DoWhileStatementNode;
        Assert.NotNull(doStmt);
        Assert.Matches("^do_auto[0-9]+$", doStmt.Id);
        Assert.Null(doStmt.ToolingId);
        Assert.Single(doStmt.Body);
        Assert.NotNull(doStmt.Condition);
    }

    [Fact]
    public void Parse_DoWhileLoop_SourceIds_ReportsError()
    {
        var source = """
            §M{m001:Test}
              §F{f001:Main:pub}
                  §DO{do1}
                  §/DO{do999} (< x INT:10)
            """;

        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.UnexpectedToken &&
            d.Message.Contains("Source IDs are removed"));
    }

    [Fact]
    public void Compile_DoWhileLoop_GeneratesValidCSharp()
    {
        var source = """
            §M{Test}
              §F{Main:pub}
                  §O{void}
                  §E{cw}
                  §B{i} INT:0
                  §DO
                    §C{Console.WriteLine}
                      §A i
                    §/C
                  §/DO (< i INT:10)
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors);
        Assert.Contains("do", result.GeneratedCode);
        Assert.Contains("while (i < 10);", result.GeneratedCode);
    }

    [Fact]
    public void BindInsideLoop_EmitsAssignmentNotRedeclaration()
    {
        // Test that mutable §B{~name} inside loop emits assignment on subsequent iterations
        // The ~ prefix indicates mutability, enabling reassignment instead of shadowing
        var source = """
            §M{Test}
              §F{SumRange:pub}
                  §I{i32:n}
                  §O{i32}
                  §B{~sum:i32} INT:0
                  §L{i:1:n:1}
                      §B{~sum:i32} (+ sum i)
                  §R sum
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
        Assert.Contains("sum = checked(sum + i);", result.GeneratedCode);
    }

    [Fact]
    public void ForLoopWithSExpressionBound_ParsesAndCompiles()
    {
        // Test §L{i:0:(- n 1):1} compiles correctly
        // Note: S-expressions in attributes cannot use INT:1 syntax because ':'
        // is the attribute separator. Use plain literals instead.
        var source = """
            §M{Test}
              §F{ProcessArray:pub}
                  §I{i32:n}
                  §O{void}
                  §E{cw}
                  §L{i:0:(- n 1):1}
                      §C{Console.WriteLine}
                        §A i
                      §/C
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message)));
        Assert.Contains("var __calorForFrom = 0;", result.GeneratedCode);
        Assert.Contains("var __calorForTo = checked(n - 1);", result.GeneratedCode);
        Assert.Contains("var __calorForStep = 1;", result.GeneratedCode);
    }

    [Fact]
    public void ForLoopWithSExpressionInFromAndStep_ParsesAndCompiles()
    {
        // Test S-expressions work in all three positions: from, to, and step
        // Note: S-expressions in attributes use plain integer literals (not INT:1 syntax)
        // because ':' is the attribute separator.
        var source = """
            §M{Test}
              §F{IterateWithExpressions:pub}
                  §I{i32:start}
                  §I{i32:end}
                  §I{i32:stepSize}
                  §O{void}
                  §E{cw}
                  §L{i:(+ start 1):(- end 1):(* stepSize 2)}
                      §C{Console.WriteLine}
                        §A i
                      §/C
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message)));
        // Verify expressions are parsed for from, to, and step
        Assert.Contains("start + 1", result.GeneratedCode);
        Assert.Contains("end - 1", result.GeneratedCode);
        Assert.Contains("stepSize * 2", result.GeneratedCode);
    }

    [Fact]
    public void ForLoopWithNestedSExpressions_ParsesAndCompiles()
    {
        // Test nested S-expressions: (+ (- a 1) (- b 2))
        var source = """
            §M{Test}
              §F{NestedExpr:pub}
                  §I{i32:a}
                  §I{i32:b}
                  §O{void}
                  §E{cw}
                  §L{i:0:(+ (- a 1) (- b 2)):1}
                      §C{Console.WriteLine}
                        §A i
                      §/C
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message)));
        // Verify nested expressions are parsed correctly
        Assert.Contains("checked(checked(a - 1) + (checked(b - 2)))", result.GeneratedCode);
    }

    [Fact]
    public void ForLoopWithComparisonInCondition_ParsesAndCompiles()
    {
        // Test comparison operators in S-expressions
        var source = """
            §M{Test}
              §F{CompareTest:pub}
                  §I{i32:limit}
                  §O{void}
                  §E{cw}
                  §B{~i:i32} INT:0
                  §WH (<= i limit)
                      §C{Console.WriteLine}
                        §A i
                      §/C
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message)));
        Assert.Contains("while (i <= limit)", result.GeneratedCode);
    }

    [Fact]
    public void LogicalOperatorsInCondition_ParsesAndCompiles()
    {
        // Test logical operators (&&, ||) in S-expressions
        var source = """
            §M{Test}
              §F{LogicalTest:pub}
                  §I{i32:a}
                  §I{i32:b}
                  §O{bool}
                  §R (&& (> a 0) (< b 100))
            """;

        var result = Program.Compile(source);

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message)));
        Assert.Contains("a > 0 && b < 100", result.GeneratedCode);
    }

    [Fact]
    public void MutableVariableReassignmentInNestedIf_EmitsCorrectly()
    {
        // Test that mutable variable reassignment works correctly in nested if blocks
        // The ~ prefix indicates mutability, enabling reassignment instead of shadowing
        var source = """
            §M{Test}
              §F{NestedIfTest:pub}
                  §I{i32:x}
                  §O{i32}
                  §B{~result:i32} INT:0
                  §IF (> x INT:0)
                      §B{~result:i32} INT:1
                      §IF (> x INT:10)
                          §B{~result:i32} INT:2
                  §R result
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
            §M{Test}
              §CL{TestClass:pub}
                  §MT{MethodA:pub}
                      §O{void}
                      §B{x:i32} INT:1
                  §MT{MethodB:pub}
                      §O{void}
                      §B{x:i32} INT:2
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
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
            §M{Test}
              §F{FloatTest:pub}
                  §I{f64:start}
                  §O{void}
                  §E{cw}
                  §L{i:0:(- start 0.5):1}
                      §C{Console.WriteLine}
                        §A i
                      §/C
            """;

        var result = Program.Compile(source);

        // This may have type issues due to mixing int loop var with float bound,
        // but it should at least parse the S-expression correctly
        Assert.Contains("start - 0.5", result.GeneratedCode);
    }

    [Fact]
    public void BindInIfBlock_ShouldShadowOuterVariable()
    {
        // According to Calor semantics S5-S6: "Inner scope does NOT mutate outer"
        // A bind in an if block should create a new variable that shadows the outer
        var source = """
            §M{Test}
              §F{testShadow:pub}
                  §I{bool:cond}
                  §O{i32}
                  §B{x:i32} INT:10
                  §IF cond
                      §B{x:i32} INT:20
                  §R x
            """;

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
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
