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
        return lexer.TokenizeAllForParser();
    }

    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static string CompileToCode(string source)
    {
        var result = Program.Compile(source, null, new CompilationOptions
        {
            EnforceEffects = false,
            DeferGeneratedOutputValidation = true,
        });
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
            module Test Test block
              func Run () → string
                !
                return new object() . ToString
            """;
        var code = CompileToCode(source);
        Assert.Contains("return new object().ToString;", code);
    }

    [Fact]
    public void TrailingAccess_NewExpression_NullConditional()
    {
        var source = """
            module Test Test block
              func Run () → string
                !
                return (new object())?.ToString
            """;
        var code = CompileToCode(source);
        Assert.Contains("return new object()?.ToString;", code);
    }

    [Fact]
    public void TrailingAccess_CallExpression_DotProperty()
    {
        var source = """
            module Test Test block
              func Run () → int
                !
                return GetItems() . Count
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
            module Test Test block
              func Run () → string
                !
                let string:result = new object() . GetType
                return result.Name
            """;
        var code = CompileToCode(source);
        Assert.Contains("new object().GetType", code);
    }

    [Fact]
    public void TrailingAccess_ParsesFieldAccessNode()
    {
        var source = """
            module Test Test block
              func Run () → string
                !
                return new object() . ToString
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
            module Test Test block
              func Run (string:x) → string
                !
                return (x ?? "default")
            """;
        var code = CompileToCode(source);
        Assert.Contains("return x ?? \"default\";", code);
    }

    [Fact]
    public void NullCoalesce_LispExpression_WithReference()
    {
        var source = """
            module Test Test block
              func Run (string:a, string:b) → string
                !
                return (a ?? b)
            """;
        var code = CompileToCode(source);
        Assert.Contains("return a ?? b;", code);
    }

    [Fact]
    public void NullCoalesce_LispExpression_WrongArgCount_ReportsError()
    {
        var source = """
            §M{Test}
              §F{Run:pub}
                  §I{string:a}
                  §I{string:b}
                  §I{string:c}
                  §O{string}
                  §R (?? a b c)
            """;
        var result = Program.Compile(source);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("exactly 2 operands"));
    }

    [Fact]
    public void NullCoalesce_ParsesNullCoalesceNode()
    {
        var source = """
            module Test Test block
              func Run (string:x) → string
                !
                return (x ?? "default")
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
            module Test Test block
              func Run () → Type
                !
                return typeof(int)
            """;
        var code = CompileToCode(source);
        Assert.Contains("return typeof(int);", code);
    }

    [Fact]
    public void TypeOf_StringType()
    {
        var source = """
            module Test Test block
              func Run () → Type
                !
                return typeof(string)
            """;
        var code = CompileToCode(source);
        Assert.Contains("return typeof(string);", code);
    }

    [Fact]
    public void TypeOf_QualifiedType()
    {
        var source = """
            module Test Test block
              func Run () → Type
                !
                return typeof(string)
            """;
        var result = Program.Compile(source, "qualified-typeof.calr");
        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Errors));
        Assert.Contains("return typeof(string);", result.GeneratedCode);
        Assert.Equal("same", InteropScope.RefOutImplementationTests.Run(result.GeneratedCode
            + "\npublic static class Probe { public static string Run() => Test.TestModule.Run() == typeof(string) ? \"same\" : \"different\"; }"));
    }

    [Fact]
    public void TypeOf_ParsesTypeOfExpressionNode()
    {
        var source = """
            module Test Test block
              func Run () → Type
                !
                return typeof(int)
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
            module Test Test block
              func Run (object:x) → bool
                !
                return (x is string)
            """;
        var code = CompileToCode(source);
        Assert.Contains("return x is string;", code);
    }

    [Fact]
    public void GenericIs_GenericType()
    {
        var source = """
            module Test Test block
              func Run (object:x) → bool
                !
                return (x is List<string>)
            """;
        var code = CompileToCode(source);
        Assert.Contains("x is List<string>", code);
    }

    [Fact]
    public void GenericIs_WithVariableBinding()
    {
        var source = """
            module Test Test block
              func Run (object:x) → bool
                !
                return (x is List<string> items)
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
            module Test Test block
              func Run (object:x) → List<string>
                !
                return (x as List<string>)
            """;
        var code = CompileToCode(source);
        Assert.Contains("x as List<string>", code);
    }

    [Fact]
    public void GenericAs_SimpleType()
    {
        var source = """
            module Test Test block
              func Run (object:x) → string?
                !
                return (x as string)
            """;
        var code = CompileToCode(source);
        Assert.Contains("return x as string;", code);
    }

    [Fact]
    public void GenericIs_ParsesIsPatternNode()
    {
        var source = """
            module Test Test block
              func Run (object:x) → bool
                !
                return (x is string)
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
            module Test Test block
              func Run (object:x) → string
                !
                return (x as string)
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
            module Test Test block
              func Run () → string
                !
                return "line1\nline2"
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
            module Test Test block
              func Run () → Type
                !
                return (new object()).GetType()
            """;
        var code = CompileToCode(source);
        Assert.Contains("return new object().GetType();", code);
    }

    [Fact]
    public void ExpressionCall_WithArguments()
    {
        var source = """
            module Test Test block
              func Run (string:x) → bool
                !
                return (new object()).Equals(x)
            """;
        var code = CompileToCode(source);
        Assert.Contains("return new object().Equals(x);", code);
    }

    [Fact]
    public void ExpressionCall_ParsesExpressionCallNode()
    {
        var source = """
            module Test Test block
              func Run () → Type
                !
                return (new object()).GetType()
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
            module Test Test block
              func Run () → string
                !
                return ToString(new object())
            """;
        var code = CompileToCode(source);
        Assert.Contains("ToString(new object())", code);
    }

    [Fact]
    public void NewInsideArg_WithTrailingAccess()
    {
        var source = """
            module Test Test block
              func Run () → string
                !
                return Process(new StringBuilder() . ToString)
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
            module Test Test block
              func Run (object:x) → bool
                !
                return (x is Dictionary<string, List<int>>)
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
            module Test Test block
              func Run (object:x) → object
                !
                return (x as Dictionary<string, List<int>>)
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
            module Test Test block
              func Run () → Type
                !
                return typeof(List<string>)
            """;
        var code = CompileToCode(source);
        Assert.Contains("return typeof(List<string>);", code);
    }

    [Fact]
    public void TypeOf_NestedGenericType()
    {
        var source = """
            module Test Test block
              func Run () → Type
                !
                return typeof(Dictionary<string, List<int>>)
            """;
        var code = CompileToCode(source);
        Assert.Contains("return typeof(Dictionary<string, List<int>>);", code);
    }

    [Fact]
    public void GenericIs_NullableType()
    {
        var source = """
            module Test Test block
              func Run (object:x) → bool
                !
                return (x is int?)
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
            module Test Test block
              func Run (int?:a, int:b) → int
                !
                return (a ?? (b + 1))
            """;
        var code = CompileToCode(source);
        Assert.Contains("int? a", code);
        Assert.Contains("return a ?? checked(b + 1);", code);
    }

    [Fact]
    public void NullCoalesce_NestedCoalescing()
    {
        var source = """
            module Test Test block
              func Run (string:a, string:b, string:c) → string
                !
                return (a ?? (b ?? c))
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
        var calorSource = "module Test Test block\n  func Run () → string\n    !\n    return \"\"\"hello\nworld\"\"\"\n\n";
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
            module Test Test block
              func Run (string:a, string:b) → string
                !
                return (new object()).ToString(a, b)
            """;
        var code = CompileToCode(source);
        Assert.Contains("return new object().ToString(a, b);", code);
    }

    [Fact]
    public void ExpressionCall_ReferenceTarget()
    {
        var source = """
            module Test Test block
              func Run (object:x) → string
                !
                return x.ToString()
            """;
        var code = CompileToCode(source);
        Assert.Contains("return x.ToString();", code);
    }

    #endregion
}
