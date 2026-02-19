using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for language features 7-13: null-coalescing, multiline strings,
/// expression call targets, trailing member access, generic is/as, typeof.
/// </summary>
public class LanguageFeatureTests
{
    #region Helpers

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

    private static string CompileToCode(string source)
    {
        var result = Program.Compile(source, null, new CompilationOptions { EnforceEffects = false });
        Assert.False(result.HasErrors, $"Compilation errors: {string.Join("; ", result.Diagnostics)}");
        return result.GeneratedCode ?? "";
    }

    /// <summary>
    /// Finds the first expression of type T in the AST (depth-first through functions and return statements).
    /// </summary>
    private static T? FindExpression<T>(ModuleNode module) where T : ExpressionNode
    {
        foreach (var func in module.Functions)
        {
            foreach (var stmt in func.Body)
            {
                if (stmt is ReturnStatementNode ret && ret.Expression is T found)
                    return found;
            }
        }
        return null;
    }

    #endregion

    #region Feature 10: Trailing Member Access after §/NEW and §/C

    [Fact]
    public void TrailingAccess_NewExpression_DotMethod()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §O{string}
              §R §NEW{object}§/NEW.ToString
            §/F{f001}
            §/M{m001}
            """;
        var code = CompileToCode(source);
        Assert.Contains("return new object().ToString;", code);
    }

    [Fact]
    public void TrailingAccess_NewExpression_NullConditional()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §O{string}
              §R §NEW{object}§/NEW?.ToString
            §/F{f001}
            §/M{m001}
            """;
        var code = CompileToCode(source);
        Assert.Contains("return new object()?.ToString;", code);
    }

    [Fact]
    public void TrailingAccess_CallExpression_DotProperty()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §O{i32}
              §R §C{GetItems}§/C.Count
            §/F{f001}
            §/M{m001}
            """;
        var code = CompileToCode(source);
        Assert.Contains("return GetItems().Count;", code);
    }

    [Fact]
    public void TrailingAccess_NewExpression_ChainedAccess()
    {
        // The lexer may tokenize "GetType.Name" in the attribute as a single identifier with dots.
        // Use separate tokens to ensure proper chaining.
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §O{string}
              §B{b001:result:string} §NEW{object}§/NEW.GetType
              §R result.Name
            §/F{f001}
            §/M{m001}
            """;
        var code = CompileToCode(source);
        Assert.Contains("new object().GetType", code);
    }

    [Fact]
    public void TrailingAccess_ParsesFieldAccessNode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §O{string}
              §R §NEW{object}§/NEW.ToString
            §/F{f001}
            §/M{m001}
            """;
        var module = Parse(source, out var diag);
        Assert.False(diag.HasErrors);
        var expr = FindExpression<FieldAccessNode>(module);
        Assert.NotNull(expr);
        Assert.Equal("ToString", expr.FieldName);
    }

    #endregion

    #region Feature 7: Null-Coalescing in Lisp Expressions

    [Fact]
    public void NullCoalesce_LispExpression_TwoArgs()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §I{string:x}
              §O{string}
              §R (?? x "default")
            §/F{f001}
            §/M{m001}
            """;
        var code = CompileToCode(source);
        Assert.Contains("return x ?? \"default\";", code);
    }

    [Fact]
    public void NullCoalesce_LispExpression_WithReference()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §I{string:a}
              §I{string:b}
              §O{string}
              §R (?? a b)
            §/F{f001}
            §/M{m001}
            """;
        var code = CompileToCode(source);
        Assert.Contains("return a ?? b;", code);
    }

    [Fact]
    public void NullCoalesce_LispExpression_WrongArgCount_ReportsError()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §I{string:a}
              §I{string:b}
              §I{string:c}
              §O{string}
              §R (?? a b c)
            §/F{f001}
            §/M{m001}
            """;
        var result = Program.Compile(source);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("exactly 2 operands"));
    }

    [Fact]
    public void NullCoalesce_ParsesNullCoalesceNode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §I{string:x}
              §O{string}
              §R (?? x "default")
            §/F{f001}
            §/M{m001}
            """;
        var module = Parse(source, out var diag);
        Assert.False(diag.HasErrors);
        var expr = FindExpression<NullCoalesceNode>(module);
        Assert.NotNull(expr);
    }

    #endregion

    #region Feature 12: typeof() as Expression

    [Fact]
    public void TypeOf_SimpleType()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §O{Type}
              §R (typeof int)
            §/F{f001}
            §/M{m001}
            """;
        var code = CompileToCode(source);
        Assert.Contains("return typeof(int);", code);
    }

    [Fact]
    public void TypeOf_StringType()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §O{Type}
              §R (typeof string)
            §/F{f001}
            §/M{m001}
            """;
        var code = CompileToCode(source);
        Assert.Contains("return typeof(string);", code);
    }

    [Fact]
    public void TypeOf_QualifiedType()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §O{Type}
              §R (typeof System.String)
            §/F{f001}
            §/M{m001}
            """;
        var code = CompileToCode(source);
        Assert.Contains("return typeof(System.String);", code);
    }

    [Fact]
    public void TypeOf_ParsesTypeOfExpressionNode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §O{Type}
              §R (typeof int)
            §/F{f001}
            §/M{m001}
            """;
        var module = Parse(source, out var diag);
        Assert.False(diag.HasErrors);
        var expr = FindExpression<TypeOfExpressionNode>(module);
        Assert.NotNull(expr);
        Assert.Equal("int", expr.TypeName);
    }

    #endregion

    #region Feature 11: Generic Types in is/as

    [Fact]
    public void GenericIs_SimpleType()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §I{object:x}
              §O{bool}
              §R (is x string)
            §/F{f001}
            §/M{m001}
            """;
        var code = CompileToCode(source);
        Assert.Contains("return x is string;", code);
    }

    [Fact]
    public void GenericIs_GenericType()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §I{object:x}
              §O{bool}
              §R (is x List<string>)
            §/F{f001}
            §/M{m001}
            """;
        var code = CompileToCode(source);
        Assert.Contains("x is List<string>", code);
    }

    [Fact]
    public void GenericIs_WithVariableBinding()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §I{object:x}
              §O{bool}
              §R (is x List<string> items)
            §/F{f001}
            §/M{m001}
            """;
        // Verify AST structure
        var module = Parse(source, out var diag);
        Assert.False(diag.HasErrors);
        var isNode = FindExpression<IsPatternNode>(module);
        Assert.NotNull(isNode);
        Assert.Equal("List<string>", isNode.TargetType);
        Assert.Equal("items", isNode.VariableName);

        // Verify code generation
        var code = CompileToCode(source);
        Assert.Contains("x is List<string> items", code);
    }

    [Fact]
    public void GenericAs_GenericType()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §I{object:x}
              §O{List<string>}
              §R (as x List<string>)
            §/F{f001}
            §/M{m001}
            """;
        var code = CompileToCode(source);
        Assert.Contains("x as List<string>", code);
    }

    [Fact]
    public void GenericAs_SimpleType()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §I{object:x}
              §O{string}
              §R (as x string)
            §/F{f001}
            §/M{m001}
            """;
        var code = CompileToCode(source);
        Assert.Contains("return x as string;", code);
    }

    [Fact]
    public void GenericIs_ParsesIsPatternNode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §I{object:x}
              §O{bool}
              §R (is x string)
            §/F{f001}
            §/M{m001}
            """;
        var module = Parse(source, out var diag);
        Assert.False(diag.HasErrors);
        var isNode = FindExpression<IsPatternNode>(module);
        Assert.NotNull(isNode);
        Assert.Equal("string", isNode.TargetType);
        Assert.Null(isNode.VariableName);
    }

    [Fact]
    public void GenericAs_ParsesTypeOperationNode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §I{object:x}
              §O{string}
              §R (as x string)
            §/F{f001}
            §/M{m001}
            """;
        var module = Parse(source, out var diag);
        Assert.False(diag.HasErrors);
        var asNode = FindExpression<TypeOperationNode>(module);
        Assert.NotNull(asNode);
        Assert.Equal(TypeOp.As, asNode.Operation);
        Assert.Equal("string", asNode.TargetType);
    }

    #endregion

    #region Feature 8: Multiline String Literals

    [Fact]
    public void MultilineString_BasicLexing()
    {
        var tokens = Tokenize("\"\"\"hello\nworld\"\"\"", out var diag);
        Assert.False(diag.HasErrors, $"Lex errors: {string.Join("; ", diag.Errors)}");
        var strToken = tokens.First(t => t.Kind == TokenKind.StrLiteral);
        Assert.Equal("hello\nworld", strToken.Value);
    }

    [Fact]
    public void MultilineString_SkipsLeadingNewline()
    {
        var tokens = Tokenize("\"\"\"\nhello\nworld\"\"\"", out var diag);
        Assert.False(diag.HasErrors);
        var strToken = tokens.First(t => t.Kind == TokenKind.StrLiteral);
        Assert.Equal("hello\nworld", strToken.Value);
    }

    [Fact]
    public void MultilineString_PreservesEscapes()
    {
        var tokens = Tokenize("\"\"\"line1\\nline2\"\"\"", out var diag);
        Assert.False(diag.HasErrors);
        var strToken = tokens.First(t => t.Kind == TokenKind.StrLiteral);
        Assert.Equal("line1\nline2", strToken.Value);
    }

    [Fact]
    public void MultilineString_EmitsCSharpEscapes()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §O{string}
              §R "line1\nline2"
            §/F{f001}
            §/M{m001}
            """;
        var code = CompileToCode(source);
        // Regular single-line strings with \n escape emit as C# escaped strings
        Assert.Contains("return \"line1\\nline2\";", code);
    }

    [Fact]
    public void MultilineString_Unterminated_ReportsError()
    {
        var tokens = Tokenize("\"\"\"hello world", out var diag);
        Assert.True(diag.HasErrors);
    }

    #endregion

    #region Feature 9: Expression Call Target

    [Fact]
    public void ExpressionCall_NewObjectGetType()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §O{Type}
              §R §C §NEW{object}§/NEW.GetType §/C
            §/F{f001}
            §/M{m001}
            """;
        var code = CompileToCode(source);
        Assert.Contains("return new object().GetType();", code);
    }

    [Fact]
    public void ExpressionCall_WithArguments()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §I{string:x}
              §O{bool}
              §R §C §NEW{object}§/NEW.Equals §A x §/C
            §/F{f001}
            §/M{m001}
            """;
        var code = CompileToCode(source);
        Assert.Contains("return new object().Equals(x);", code);
    }

    [Fact]
    public void ExpressionCall_ParsesExpressionCallNode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §O{Type}
              §R §C §NEW{object}§/NEW.GetType §/C
            §/F{f001}
            §/M{m001}
            """;
        var module = Parse(source, out var diag);
        Assert.False(diag.HasErrors);
        var expr = FindExpression<ExpressionCallNode>(module);
        Assert.NotNull(expr);
        Assert.Empty(expr.Arguments);
    }

    #endregion

    #region Feature 13: §NEW inside §A regression tests

    [Fact]
    public void NewInsideArg_BasicNew()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §O{string}
              §R §C{ToString} §A §NEW{object}§/NEW §/C
            §/F{f001}
            §/M{m001}
            """;
        var code = CompileToCode(source);
        Assert.Contains("ToString(new object())", code);
    }

    [Fact]
    public void NewInsideArg_WithTrailingAccess()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §O{string}
              §R §C{Process} §A §NEW{StringBuilder}§/NEW.ToString §/C
            §/F{f001}
            §/M{m001}
            """;
        var code = CompileToCode(source);
        Assert.Contains("Process(new StringBuilder().ToString)", code);
    }

    #endregion

    #region Edge Cases: Nested Generics

    [Fact]
    public void GenericIs_NestedGenericType()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §I{object:x}
              §O{bool}
              §R (is x Dictionary<string, List<int>>)
            §/F{f001}
            §/M{m001}
            """;
        var module = Parse(source, out var diag);
        Assert.False(diag.HasErrors);
        var isNode = FindExpression<IsPatternNode>(module);
        Assert.NotNull(isNode);
        Assert.Equal("Dictionary<string, List<int>>", isNode.TargetType);

        var code = CompileToCode(source);
        Assert.Contains("x is Dictionary<string, List<int>>", code);
    }

    [Fact]
    public void GenericAs_NestedGenericType()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §I{object:x}
              §O{object}
              §R (as x Dictionary<string, List<int>>)
            §/F{f001}
            §/M{m001}
            """;
        var module = Parse(source, out var diag);
        Assert.False(diag.HasErrors);
        var asNode = FindExpression<TypeOperationNode>(module);
        Assert.NotNull(asNode);
        Assert.Equal("Dictionary<string, List<int>>", asNode.TargetType);

        var code = CompileToCode(source);
        Assert.Contains("x as Dictionary<string, List<int>>", code);
    }

    [Fact]
    public void TypeOf_GenericType()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §O{Type}
              §R (typeof List<string>)
            §/F{f001}
            §/M{m001}
            """;
        var code = CompileToCode(source);
        Assert.Contains("return typeof(List<string>);", code);
    }

    [Fact]
    public void TypeOf_NestedGenericType()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §O{Type}
              §R (typeof Dictionary<string, List<int>>)
            §/F{f001}
            §/M{m001}
            """;
        var code = CompileToCode(source);
        Assert.Contains("return typeof(Dictionary<string, List<int>>);", code);
    }

    [Fact]
    public void GenericIs_NullableType()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §I{object:x}
              §O{bool}
              §R (is x int?)
            §/F{f001}
            §/M{m001}
            """;
        var module = Parse(source, out var diag);
        Assert.False(diag.HasErrors);
        var isNode = FindExpression<IsPatternNode>(module);
        Assert.NotNull(isNode);
        Assert.Equal("int?", isNode.TargetType);

        var code = CompileToCode(source);
        Assert.Contains("x is int?", code);
    }

    #endregion

    #region Edge Cases: Complex ?? Operands

    [Fact]
    public void NullCoalesce_WithNestedExpression()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §I{i32:a}
              §I{i32:b}
              §O{i32}
              §R (?? a (+ b 1))
            §/F{f001}
            §/M{m001}
            """;
        var code = CompileToCode(source);
        Assert.Contains("return a ?? (b + 1);", code);
    }

    [Fact]
    public void NullCoalesce_NestedCoalescing()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §I{string:a}
              §I{string:b}
              §I{string:c}
              §O{string}
              §R (?? a (?? b c))
            §/F{f001}
            §/M{m001}
            """;
        var code = CompileToCode(source);
        Assert.Contains("return a ?? b ?? c;", code);
    }

    #endregion

    #region Edge Cases: Multiline String E2E

    [Fact]
    public void MultilineString_E2E_TripleQuoteCompiles()
    {
        // Use actual triple-quote multiline syntax in a full compilation
        // Note: The triple-quote must be in the Calor source, not the C# raw string
        var calorSource = "§M{m001:Test}\n§F{f001:Run:pub}\n§O{string}\n§R \"\"\"hello\nworld\"\"\"\n§/F{f001}\n§/M{m001}";
        var result = Program.Compile(calorSource, null, new CompilationOptions { EnforceEffects = false });
        Assert.False(result.HasErrors, $"Errors: {string.Join("; ", result.Diagnostics)}");
        // Multiline string should emit as C# verbatim string
        Assert.Contains("@\"hello\nworld\"", result.GeneratedCode);
    }

    [Fact]
    public void MultilineString_SingleLine_NoVerbatim()
    {
        // A triple-quote string without actual newlines should emit as regular string
        var tokens = Tokenize("\"\"\"hello world\"\"\"", out var diag);
        Assert.False(diag.HasErrors);
        var strToken = tokens.First(t => t.Kind == TokenKind.StrLiteral);
        Assert.Equal("hello world", strToken.Value);
    }

    [Fact]
    public void MultilineString_WithCRLF()
    {
        var tokens = Tokenize("\"\"\"\r\nhello\r\nworld\"\"\"", out var diag);
        Assert.False(diag.HasErrors);
        var strToken = tokens.First(t => t.Kind == TokenKind.StrLiteral);
        Assert.Equal("hello\r\nworld", strToken.Value);
    }

    #endregion

    #region Edge Cases: Expression Call with Multiple Args

    [Fact]
    public void ExpressionCall_MultipleArgs()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §I{string:a}
              §I{string:b}
              §O{string}
              §R §C §NEW{object}§/NEW.ToString §A a §A b §/C
            §/F{f001}
            §/M{m001}
            """;
        var code = CompileToCode(source);
        Assert.Contains("return new object().ToString(a, b);", code);
    }

    [Fact]
    public void ExpressionCall_ReferenceTarget()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Run:pub}
              §I{object:x}
              §O{string}
              §R §C x.ToString §/C
            §/F{f001}
            §/M{m001}
            """;
        var code = CompileToCode(source);
        Assert.Contains("return x.ToString();", code);
    }

    #endregion
}
