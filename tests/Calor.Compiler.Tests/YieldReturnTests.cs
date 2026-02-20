using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class YieldReturnTests
{
    private readonly CSharpToCalorConverter _converter = new();

    private static List<Token> Tokenize(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        return lexer.TokenizeAll();
    }

    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Success) return string.Empty;
        return string.Join("\n", result.Issues.Select(i => i.ToString()));
    }

    #region Lexer Tests

    [Fact]
    public void Lexer_Yield_TokenizesCorrectly()
    {
        var tokens = Tokenize("§YIELD", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Contains(tokens, t => t.Kind == TokenKind.Yield);
    }

    [Fact]
    public void Lexer_YieldBreak_TokenizesCorrectly()
    {
        var tokens = Tokenize("§YBRK", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Contains(tokens, t => t.Kind == TokenKind.YieldBreak);
    }

    #endregion

    #region Parser Tests

    [Fact]
    public void Parser_YieldReturn_ParsesExpression()
    {
        var source = """
            §M{m1:TestModule}
              §F{f1:GetNumbers:pub}
                §O{i32}
                §YIELD 42
              §/F{f1}
            §/M{m1}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var func = Assert.Single(module.Functions);
        var yieldStmt = Assert.IsType<YieldReturnStatementNode>(func.Body[0]);
        Assert.NotNull(yieldStmt.Expression);
        var lit = Assert.IsType<IntLiteralNode>(yieldStmt.Expression);
        Assert.Equal(42, lit.Value);
    }

    [Fact]
    public void Parser_YieldBreak_ParsesCorrectly()
    {
        var source = """
            §M{m1:TestModule}
              §F{f1:GetNumbers:pub}
                §O{i32}
                §YBRK
              §/F{f1}
            §/M{m1}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var func = Assert.Single(module.Functions);
        Assert.IsType<YieldBreakStatementNode>(func.Body[0]);
    }

    [Fact]
    public void Parser_YieldReturnWithReference_ParsesCorrectly()
    {
        var source = """
            §M{m1:TestModule}
              §F{f1:GetItems:pub}
                §I{i32:x}
                §O{i32}
                §YIELD x
              §/F{f1}
            §/M{m1}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var func = Assert.Single(module.Functions);
        var yieldStmt = Assert.IsType<YieldReturnStatementNode>(func.Body[0]);
        Assert.NotNull(yieldStmt.Expression);
        var refNode = Assert.IsType<ReferenceNode>(yieldStmt.Expression);
        Assert.Equal("x", refNode.Name);
    }

    #endregion

    #region CSharpEmitter Tests

    [Fact]
    public void CSharpEmitter_YieldReturn_EmitsCorrectly()
    {
        var span = new TextSpan(0, 0, 0, 0);
        var expr = new IntLiteralNode(span, 42);
        var node = new YieldReturnStatementNode(span, expr);

        var emitter = new CSharpEmitter();
        var output = node.Accept(emitter);

        Assert.Equal("yield return 42;", output);
    }

    [Fact]
    public void CSharpEmitter_YieldReturnNoExpression_EmitsCorrectly()
    {
        var span = new TextSpan(0, 0, 0, 0);
        var node = new YieldReturnStatementNode(span, null);

        var emitter = new CSharpEmitter();
        var output = node.Accept(emitter);

        Assert.Equal("yield return;", output);
    }

    [Fact]
    public void CSharpEmitter_YieldBreak_EmitsCorrectly()
    {
        var span = new TextSpan(0, 0, 0, 0);
        var node = new YieldBreakStatementNode(span);

        var emitter = new CSharpEmitter();
        var output = node.Accept(emitter);

        Assert.Equal("yield break;", output);
    }

    [Fact]
    public void CSharpEmitter_IteratorFunction_WrapsReturnType()
    {
        var source = """
            §M{m1:TestModule}
              §F{f1:GetNumbers:pub}
                §O{i32}
                §YIELD 1
                §YIELD 2
                §YIELD 3
              §/F{f1}
            §/M{m1}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var output = emitter.Emit(module);

        Assert.Contains("IEnumerable<int>", output);
        Assert.Contains("yield return 1;", output);
        Assert.Contains("yield return 2;", output);
        Assert.Contains("yield return 3;", output);
    }

    [Fact]
    public void CSharpEmitter_IteratorMethod_WrapsReturnType()
    {
        var csharp = """
            public class NumberGenerator
            {
                public IEnumerable<int> GetNumbers()
                {
                    yield return 1;
                    yield return 2;
                    yield break;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CSharpEmitter();
        var output = emitter.Emit(result.Ast!);

        Assert.Contains("IEnumerable<int>", output);
        Assert.Contains("yield return", output);
        Assert.Contains("yield break;", output);
    }

    #endregion

    #region CalorEmitter Tests

    [Fact]
    public void CalorEmitter_YieldReturn_EmitsCalorSyntax()
    {
        var csharp = """
            public class NumberGenerator
            {
                public IEnumerable<int> GetNumbers()
                {
                    yield return 42;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CalorEmitter();
        var output = emitter.Emit(result.Ast!);

        Assert.Contains("§YIELD", output);
    }

    [Fact]
    public void CalorEmitter_YieldBreak_EmitsCalorSyntax()
    {
        var csharp = """
            public class NumberGenerator
            {
                public IEnumerable<int> GetNumbers()
                {
                    yield break;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CalorEmitter();
        var output = emitter.Emit(result.Ast!);

        Assert.Contains("§YBRK", output);
    }

    [Fact]
    public void CalorEmitter_Roundtrip_YieldSyntax()
    {
        var csharp = """
            public class NumberGenerator
            {
                public IEnumerable<int> GetNumbers()
                {
                    yield return 1;
                    yield return 2;
                    yield break;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CalorEmitter();
        var output = emitter.Emit(result.Ast!);

        Assert.Contains("§YIELD", output);
        Assert.Contains("§YBRK", output);
    }

    #endregion

    #region Converter Tests

    [Fact]
    public void Converter_YieldReturn_CreatesYieldReturnNode()
    {
        var csharp = """
            public class NumberGenerator
            {
                public IEnumerable<int> GetNumbers()
                {
                    yield return 42;
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        var yieldStmt = Assert.IsType<YieldReturnStatementNode>(method.Body[0]);
        Assert.NotNull(yieldStmt.Expression);
    }

    [Fact]
    public void Converter_YieldBreak_CreatesYieldBreakNode()
    {
        var csharp = """
            public class NumberGenerator
            {
                public IEnumerable<int> GetNumbers()
                {
                    yield break;
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        Assert.IsType<YieldBreakStatementNode>(method.Body[0]);
    }

    [Fact]
    public void Converter_YieldReturnWithExpression_PreservesExpression()
    {
        var csharp = """
            public class Service
            {
                public IEnumerable<string> GetNames()
                {
                    yield return "Alice";
                    yield return "Bob";
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        Assert.Equal(2, method.Body.Count);
        Assert.All(method.Body, stmt => Assert.IsType<YieldReturnStatementNode>(stmt));
    }

    [Fact]
    public void Converter_MixedYieldStatements_ConvertsBoth()
    {
        var csharp = """
            public class Service
            {
                public IEnumerable<int> Range(int start, int end)
                {
                    for (int i = start; i < end; i++)
                    {
                        yield return i;
                    }
                    yield break;
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);

        // Method should contain a for loop and a yield break
        bool hasYieldReturn = false;
        bool hasYieldBreak = false;

        foreach (var stmt in method.Body)
        {
            if (stmt is YieldBreakStatementNode) hasYieldBreak = true;
            if (stmt is ForStatementNode forStmt)
            {
                hasYieldReturn = forStmt.Body.Any(s => s is YieldReturnStatementNode);
            }
        }

        Assert.True(hasYieldReturn, "Should contain yield return inside for loop");
        Assert.True(hasYieldBreak, "Should contain yield break");
    }

    #endregion

    #region E2E Roundtrip Tests

    [Fact]
    public void E2E_YieldReturn_RoundtripsCorrectly()
    {
        var csharp = """
            public class NumberGenerator
            {
                public IEnumerable<int> GetNumbers()
                {
                    yield return 1;
                    yield return 2;
                    yield return 3;
                }
            }
            """;

        // Convert C# → Calor AST
        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        // Emit back to C#
        var emitter = new CSharpEmitter();
        var output = emitter.Emit(result.Ast!);

        Assert.Contains("IEnumerable<int>", output);
        Assert.Contains("yield return 1;", output);
        Assert.Contains("yield return 2;", output);
        Assert.Contains("yield return 3;", output);
    }

    [Fact]
    public void E2E_YieldBreak_RoundtripsCorrectly()
    {
        var csharp = """
            public class Service
            {
                public IEnumerable<string> GetItems(bool flag)
                {
                    if (!flag)
                    {
                        yield break;
                    }
                    yield return "item";
                }
            }
            """;

        // Convert C# → Calor AST
        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        // Emit back to C#
        var emitter = new CSharpEmitter();
        var output = emitter.Emit(result.Ast!);

        Assert.Contains("yield break;", output);
        Assert.Contains("yield return", output);
    }

    [Fact]
    public void E2E_CalorSource_WithYield_CompilesToCSharp()
    {
        var calorSource = """
            §M{m1:TestModule}
              §F{f1:GetNumbers:pub}
                §O{i32}
                §YIELD 1
                §YIELD 2
                §YIELD 3
              §/F{f1}
            §/M{m1}
            """;

        var compilationResult = Program.Compile(calorSource);

        Assert.False(compilationResult.HasErrors,
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));
        Assert.Contains("IEnumerable<int>", compilationResult.GeneratedCode);
        Assert.Contains("yield return 1;", compilationResult.GeneratedCode);
    }

    [Fact]
    public void E2E_CalorSource_WithYieldBreak_CompilesToCSharp()
    {
        var calorSource = """
            §M{m1:TestModule}
              §F{f1:GetNumbers:pub}
                §O{i32}
                §YIELD 1
                §YBRK
              §/F{f1}
            §/M{m1}
            """;

        var compilationResult = Program.Compile(calorSource);

        Assert.False(compilationResult.HasErrors,
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));
        Assert.Contains("yield return 1;", compilationResult.GeneratedCode);
        Assert.Contains("yield break;", compilationResult.GeneratedCode);
    }

    #endregion

    #region Calor Class Method with Yield

    [Fact]
    public void E2E_CalorClassMethod_WithYield_WrapsReturnType()
    {
        var calorSource = """
            §M{m1:TestModule}
              §CL{c1:NumberGenerator}
                §MT{m1:GetNumbers:pub}
                  §O{i32}
                  §YIELD 1
                  §YIELD 2
                  §YIELD 3
                §/MT{m1}
              §/CL{c1}
            §/M{m1}
            """;

        var compilationResult = Program.Compile(calorSource);

        Assert.False(compilationResult.HasErrors,
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));
        Assert.Contains("IEnumerable<int>", compilationResult.GeneratedCode);
        Assert.Contains("yield return 1;", compilationResult.GeneratedCode);
    }

    [Fact]
    public void E2E_CalorClassMethod_WithYieldBreak_CompilesToCSharp()
    {
        var calorSource = """
            §M{m1:TestModule}
              §CL{c1:Service}
                §MT{m1:GetItems:pub}
                  §O{string}
                  §YIELD "hello"
                  §YBRK
                §/MT{m1}
              §/CL{c1}
            §/M{m1}
            """;

        var compilationResult = Program.Compile(calorSource);

        Assert.False(compilationResult.HasErrors,
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));
        Assert.Contains("IEnumerable<string>", compilationResult.GeneratedCode);
        Assert.Contains("yield break;", compilationResult.GeneratedCode);
    }

    #endregion

    #region CalorEmitter → Parser Roundtrip

    [Fact]
    public void Roundtrip_CalorEmitter_YieldReturn_ReParses()
    {
        var csharp = """
            public class NumberGenerator
            {
                public IEnumerable<int> GetNumbers()
                {
                    yield return 1;
                    yield return 2;
                    yield break;
                }
            }
            """;

        // C# → Calor AST
        var conversionResult = _converter.Convert(csharp);
        Assert.True(conversionResult.Success, GetErrorMessage(conversionResult));

        // AST → Calor source
        var calorEmitter = new CalorEmitter();
        var calorSource = calorEmitter.Emit(conversionResult.Ast!);
        Assert.Contains("§YIELD", calorSource);
        Assert.Contains("§YBRK", calorSource);

        // Calor source → re-parse → C#
        var compilationResult = Program.Compile(calorSource);
        Assert.False(compilationResult.HasErrors,
            "Roundtrip re-parse failed:\n" +
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));
        Assert.Contains("yield return", compilationResult.GeneratedCode);
        Assert.Contains("yield break;", compilationResult.GeneratedCode);
    }

    [Fact]
    public void Roundtrip_ConversionCalorSource_YieldReturn_ReParses()
    {
        var csharp = """
            public class Service
            {
                public IEnumerable<string> GetNames(bool includeAdmin)
                {
                    yield return "Alice";
                    yield return "Bob";
                    if (includeAdmin)
                    {
                        yield return "Admin";
                    }
                    yield break;
                }
            }
            """;

        // C# → Calor source (via converter)
        var conversionResult = _converter.Convert(csharp);
        Assert.True(conversionResult.Success, GetErrorMessage(conversionResult));
        Assert.NotNull(conversionResult.CalorSource);

        // Calor source → re-parse → C#
        var compilationResult = Program.Compile(conversionResult.CalorSource!);
        Assert.False(compilationResult.HasErrors,
            "Roundtrip via CalorSource failed:\n" +
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));
        Assert.Contains("yield return", compilationResult.GeneratedCode);
        Assert.Contains("yield break;", compilationResult.GeneratedCode);
    }

    #endregion

    #region DoWhile Yield Detection

    [Fact]
    public void CSharpEmitter_YieldInsideDoWhile_WrapsReturnType()
    {
        var csharp = """
            public class Service
            {
                public IEnumerable<int> Generate()
                {
                    int i = 0;
                    do
                    {
                        yield return i;
                        i++;
                    } while (i < 10);
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CSharpEmitter();
        var output = emitter.Emit(result.Ast!);

        Assert.Contains("IEnumerable<int>", output);
        Assert.Contains("yield return", output);
    }

    #endregion

    #region Parameter Modifier Roundtrip

    [Fact]
    public void Roundtrip_RefParameter_PreservedThroughCalorEmitter()
    {
        var csharp = """
            public class Service
            {
                public void Swap(ref int a, ref int b)
                {
                    int temp = a;
                    a = b;
                    b = temp;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        // Verify converter detected ref
        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        Assert.True(method.Parameters[0].Modifier.HasFlag(ParameterModifier.Ref));
        Assert.True(method.Parameters[1].Modifier.HasFlag(ParameterModifier.Ref));

        // CalorEmitter should include ref in output
        var calorEmitter = new CalorEmitter();
        var calorOutput = calorEmitter.Emit(result.Ast!);
        Assert.Contains("ref", calorOutput);

        // CSharpEmitter should emit ref keyword
        var csharpEmitter = new CSharpEmitter();
        var csharpOutput = csharpEmitter.Emit(result.Ast!);
        Assert.Contains("ref int", csharpOutput);
    }

    [Fact]
    public void Roundtrip_OutParameter_PreservedThroughCalorEmitter()
    {
        var csharp = """
            public class Service
            {
                public bool TryParse(string input, out int result)
                {
                    result = 0;
                    return true;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        Assert.True(method.Parameters[1].Modifier.HasFlag(ParameterModifier.Out));

        var calorEmitter = new CalorEmitter();
        var calorOutput = calorEmitter.Emit(result.Ast!);
        Assert.Contains("out", calorOutput);

        var csharpEmitter = new CSharpEmitter();
        var csharpOutput = csharpEmitter.Emit(result.Ast!);
        Assert.Contains("out int", csharpOutput);
    }

    [Fact]
    public void Roundtrip_ParamsParameter_PreservedThroughCalorEmitter()
    {
        var csharp = """
            public class Service
            {
                public int Sum(params int[] values)
                {
                    return 0;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var cls = Assert.Single(result.Ast!.Classes);
        var method = Assert.Single(cls.Methods);
        Assert.True(method.Parameters[0].Modifier.HasFlag(ParameterModifier.Params));

        var calorEmitter = new CalorEmitter();
        var calorOutput = calorEmitter.Emit(result.Ast!);
        Assert.Contains("params", calorOutput);

        var csharpEmitter = new CSharpEmitter();
        var csharpOutput = csharpEmitter.Emit(result.Ast!);
        Assert.Contains("params int[]", csharpOutput);
    }

    [Fact]
    public void Parser_RefModifier_ParsedFromCalorSyntax()
    {
        var source = """
            §M{m1:TestModule}
              §F{f1:Process:pub}
                §I{i32:value:ref}
                §O{void}
              §/F{f1}
            §/M{m1}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var func = Assert.Single(module.Functions);
        var param = Assert.Single(func.Parameters);
        Assert.True(param.Modifier.HasFlag(ParameterModifier.Ref));
    }

    [Fact]
    public void Parser_OutModifier_ParsedFromCalorSyntax()
    {
        var source = """
            §M{m1:TestModule}
              §F{f1:Process:pub}
                §I{i32:value:out}
                §O{void}
              §/F{f1}
            §/M{m1}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var func = Assert.Single(module.Functions);
        var param = Assert.Single(func.Parameters);
        Assert.True(param.Modifier.HasFlag(ParameterModifier.Out));
    }

    [Fact]
    public void Parser_ParamsModifier_ParsedFromCalorSyntax()
    {
        var source = """
            §M{m1:TestModule}
              §F{f1:Process:pub}
                §I{string:values:params}
                §O{void}
              §/F{f1}
            §/M{m1}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var func = Assert.Single(module.Functions);
        var param = Assert.Single(func.Parameters);
        Assert.True(param.Modifier.HasFlag(ParameterModifier.Params));
    }

    #endregion

    #region FeatureSupport

    [Fact]
    public void FeatureSupport_YieldReturn_IsFullySupported()
    {
        Assert.True(FeatureSupport.IsFullySupported("yield-return"));
    }

    #endregion
}
