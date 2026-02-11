using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class StringOperationTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static string WrapInFunction(string body)
    {
        return $$"""
            §M{m001:Test}
            §F{f001:Main:pub}
              §I{string:s}
              §O{string}
              {{body}}
            §/F{f001}
            §/M{m001}
            """;
    }

    private static string WrapInFunctionWithParams(string body, string paramStr)
    {
        return $$"""
            §M{m001:Test}
            §F{f001:Main:pub}
              {{paramStr}}
              §O{string}
              {{body}}
            §/F{f001}
            §/M{m001}
            """;
    }

    private static StringOperationNode GetReturnExpression(ModuleNode module)
    {
        var func = module.Functions[0];
        var returnStmt = func.Body[0] as ReturnStatementNode;
        Assert.NotNull(returnStmt);
        var strOp = returnStmt!.Expression as StringOperationNode;
        Assert.NotNull(strOp);
        return strOp!;
    }

    private static ExpressionNode GetBindExpression(ModuleNode module)
    {
        var func = module.Functions[0];
        var bindStmt = func.Body[0] as BindStatementNode;
        Assert.NotNull(bindStmt);
        return bindStmt!.Initializer!;
    }

    #region Phase 1: AST Correctness Tests

    [Fact]
    public void Parse_Length_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (len s)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.Length, strOp.Operation);
        Assert.Single(strOp.Arguments);
    }

    [Fact]
    public void Parse_Contains_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (contains s \"hello\")");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.Contains, strOp.Operation);
        Assert.Equal(2, strOp.Arguments.Count);
    }

    [Fact]
    public void Parse_StartsWith_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (starts s \"prefix\")");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.StartsWith, strOp.Operation);
        Assert.Equal(2, strOp.Arguments.Count);
    }

    [Fact]
    public void Parse_EndsWith_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (ends s \"suffix\")");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.EndsWith, strOp.Operation);
        Assert.Equal(2, strOp.Arguments.Count);
    }

    [Fact]
    public void Parse_IndexOf_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (indexof s \"x\")");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.IndexOf, strOp.Operation);
        Assert.Equal(2, strOp.Arguments.Count);
    }

    [Fact]
    public void Parse_SubstringWithLength_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (substr s 0 5)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.Substring, strOp.Operation);
        Assert.Equal(3, strOp.Arguments.Count);
    }

    [Fact]
    public void Parse_SubstringFrom_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (substr s 5)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.SubstringFrom, strOp.Operation);
        Assert.Equal(2, strOp.Arguments.Count);
    }

    [Fact]
    public void Parse_Replace_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (replace s \"old\" \"new\")");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.Replace, strOp.Operation);
        Assert.Equal(3, strOp.Arguments.Count);
    }

    [Fact]
    public void Parse_ToUpper_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (upper s)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.ToUpper, strOp.Operation);
        Assert.Single(strOp.Arguments);
    }

    [Fact]
    public void Parse_ToLower_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (lower s)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.ToLower, strOp.Operation);
        Assert.Single(strOp.Arguments);
    }

    [Fact]
    public void Parse_Trim_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (trim s)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.Trim, strOp.Operation);
        Assert.Single(strOp.Arguments);
    }

    [Fact]
    public void Parse_TrimStart_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (ltrim s)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.TrimStart, strOp.Operation);
        Assert.Single(strOp.Arguments);
    }

    [Fact]
    public void Parse_TrimEnd_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (rtrim s)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.TrimEnd, strOp.Operation);
        Assert.Single(strOp.Arguments);
    }

    [Fact]
    public void Parse_PadLeft_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (lpad s 10)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.PadLeft, strOp.Operation);
        Assert.Equal(2, strOp.Arguments.Count);
    }

    [Fact]
    public void Parse_PadRight_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (rpad s 10)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.PadRight, strOp.Operation);
        Assert.Equal(2, strOp.Arguments.Count);
    }

    [Fact]
    public void Parse_Join_ReturnsStringOperationNode()
    {
        // Test that join parses correctly - use string variables to avoid array syntax complexity
        var source = WrapInFunctionWithParams("§R (join \", \" items)", "§I{object:items}");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.Join, strOp.Operation);
        Assert.Equal(2, strOp.Arguments.Count);
    }

    [Fact]
    public void Parse_Format_ReturnsStringOperationNode()
    {
        var source = WrapInFunctionWithParams("§R (fmt \"Hello {0}\" name)", "§I{string:name}");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.Format, strOp.Operation);
        Assert.Equal(2, strOp.Arguments.Count);
    }

    [Fact]
    public void Parse_Concat_ReturnsStringOperationNode()
    {
        var source = WrapInFunctionWithParams("§R (concat a b c)", "§I{string:a} §I{string:b} §I{string:c}");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.Concat, strOp.Operation);
        Assert.Equal(3, strOp.Arguments.Count);
    }

    [Fact]
    public void Parse_IsNullOrEmpty_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (isempty s)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.IsNullOrEmpty, strOp.Operation);
        Assert.Single(strOp.Arguments);
    }

    [Fact]
    public void Parse_IsNullOrWhiteSpace_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (isblank s)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.IsNullOrWhiteSpace, strOp.Operation);
        Assert.Single(strOp.Arguments);
    }

    [Fact]
    public void Parse_Split_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (split s \",\")");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.Split, strOp.Operation);
        Assert.Equal(2, strOp.Arguments.Count);
    }

    [Fact]
    public void Parse_ToString_ReturnsStringOperationNode()
    {
        var source = WrapInFunctionWithParams("§R (str x)", "§I{i32:x}");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.ToString, strOp.Operation);
        Assert.Single(strOp.Arguments);
    }

    #endregion

    #region Phase 2: C# Emission Tests

    [Theory]
    [InlineData("(len s)", "s.Length")]
    [InlineData("(contains s \"x\")", "s.Contains(\"x\")")]
    [InlineData("(starts s \"x\")", "s.StartsWith(\"x\")")]
    [InlineData("(ends s \"x\")", "s.EndsWith(\"x\")")]
    [InlineData("(indexof s \"x\")", "s.IndexOf(\"x\")")]
    [InlineData("(substr s 0 5)", "s.Substring(0, 5)")]
    [InlineData("(substr s 5)", "s.Substring(5)")]
    [InlineData("(replace s \"a\" \"b\")", "s.Replace(\"a\", \"b\")")]
    [InlineData("(upper s)", "s.ToUpper()")]
    [InlineData("(lower s)", "s.ToLower()")]
    [InlineData("(trim s)", "s.Trim()")]
    [InlineData("(ltrim s)", "s.TrimStart()")]
    [InlineData("(rtrim s)", "s.TrimEnd()")]
    [InlineData("(lpad s 10)", "s.PadLeft(10)")]
    [InlineData("(rpad s 10)", "s.PadRight(10)")]
    [InlineData("(isempty s)", "string.IsNullOrEmpty(s)")]
    [InlineData("(isblank s)", "string.IsNullOrWhiteSpace(s)")]
    [InlineData("(split s \",\")", "s.Split(\",\")")]
    [InlineData("(str s)", "s.ToString()")]
    public void Emit_StringOperation_ProducesCorrectCSharp(string calor, string expectedCSharp)
    {
        var source = WrapInFunction($"§R {calor}");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains(expectedCSharp, code);
    }

    [Fact]
    public void Emit_Join_ProducesCorrectCSharp()
    {
        // Test that join emits correctly - use object type to avoid array syntax complexity
        var source = WrapInFunctionWithParams("§R (join \",\" items)", "§I{object:items}");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("string.Join(\",\", items)", code);
    }

    [Fact]
    public void Emit_Format_ProducesCorrectCSharp()
    {
        var source = WrapInFunctionWithParams("§R (fmt \"Hello {0}\" name)", "§I{string:name}");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("string.Format(\"Hello {0}\", name)", code);
    }

    [Fact]
    public void Emit_Concat_ProducesCorrectCSharp()
    {
        var source = WrapInFunctionWithParams("§R (concat a b c)", "§I{string:a} §I{string:b} §I{string:c}");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("string.Concat(a, b, c)", code);
    }

    #endregion

    #region Phase 3: Error Cases

    [Fact]
    public void Parse_Upper_NoArgs_ReportsError()
    {
        var source = WrapInFunction("§R (upper)");
        Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Message.Contains("requires at least 1 argument"));
    }

    [Fact]
    public void Parse_Upper_TooManyArgs_ReportsError()
    {
        var source = WrapInFunction("§R (upper s \"extra\")");
        Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Message.Contains("accepts at most 1 argument"));
    }

    [Fact]
    public void Parse_Contains_OneArg_ReportsError()
    {
        var source = WrapInFunction("§R (contains s)");
        Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Message.Contains("requires at least 2 argument"));
    }

    [Fact]
    public void Parse_Substr_OneArg_ReportsError()
    {
        var source = WrapInFunction("§R (substr s)");
        Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Message.Contains("requires at least 2 argument"));
    }

    #endregion

    #region Phase 4: Composition Tests (Nested Operations)

    [Fact]
    public void Parse_Nested_UpperTrim_ReturnsCorrectAst()
    {
        var source = WrapInFunction("§R (upper (trim s))");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.ToUpper, strOp.Operation);
        Assert.Single(strOp.Arguments);
        var inner = strOp.Arguments[0] as StringOperationNode;
        Assert.NotNull(inner);
        Assert.Equal(StringOp.Trim, inner!.Operation);
    }

    [Fact]
    public void Emit_Nested_UpperTrim_ProducesCorrectCSharp()
    {
        var source = WrapInFunction("§R (upper (trim s))");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("s.Trim().ToUpper()", code);
    }

    [Fact]
    public void Parse_Nested_ContainsLower_ReturnsCorrectAst()
    {
        var source = WrapInFunction("§R (contains (lower s) \"hello\")");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.Contains, strOp.Operation);
        Assert.Equal(2, strOp.Arguments.Count);
        var inner = strOp.Arguments[0] as StringOperationNode;
        Assert.NotNull(inner);
        Assert.Equal(StringOp.ToLower, inner!.Operation);
    }

    [Fact]
    public void Emit_Nested_ContainsLower_ProducesCorrectCSharp()
    {
        var source = WrapInFunction("§R (contains (lower s) \"hello\")");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("s.ToLower().Contains(\"hello\")", code);
    }

    [Fact]
    public void Emit_Nested_ThreeDeep_ProducesCorrectCSharp()
    {
        var source = WrapInFunction("§R (len (upper (trim s)))");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("s.Trim().ToUpper().Length", code);
    }

    #endregion

    #region Phase 5: Expression Context Tests

    [Fact]
    public void Parse_StringOp_InCondition_Works()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §I{string:s}
              §O{string}
              §IF{if1} (isempty s)
                §R ""
              §/I{if1}
              §R s
            §/F{f001}
            §/M{m001}
            """;
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        var ifStmt = func.Body[0] as IfStatementNode;
        Assert.NotNull(ifStmt);
        var cond = ifStmt!.Condition as StringOperationNode;
        Assert.NotNull(cond);
        Assert.Equal(StringOp.IsNullOrEmpty, cond!.Operation);
    }

    [Fact]
    public void Parse_StringOp_InBinaryExpression_Works()
    {
        var source = WrapInFunction("§R (> (len s) 10)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        var returnStmt = func.Body[0] as ReturnStatementNode;
        Assert.NotNull(returnStmt);
        // Return is a binary operation containing a string operation
        var binOp = returnStmt!.Expression as BinaryOperationNode;
        Assert.NotNull(binOp);
        Assert.Equal(BinaryOperator.GreaterThan, binOp!.Operator);
        // Left operand should be the string length operation
        var strOp = binOp.Left as StringOperationNode;
        Assert.NotNull(strOp);
        Assert.Equal(StringOp.Length, strOp!.Operation);
    }

    [Fact]
    public void Parse_StringOp_InBinding_Works()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §I{string:s}
              §O{string}
              §B{x} (upper (trim s))
              §R x
            §/F{f001}
            §/M{m001}
            """;
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var expr = GetBindExpression(module);
        var strOp = expr as StringOperationNode;
        Assert.NotNull(strOp);
        Assert.Equal(StringOp.ToUpper, strOp!.Operation);
    }

    #endregion

    #region Phase 6: Round-Trip Tests (CalorEmitter)

    [Theory]
    [InlineData("(len s)")]
    [InlineData("(contains s \"x\")")]
    [InlineData("(starts s \"x\")")]
    [InlineData("(ends s \"x\")")]
    [InlineData("(indexof s \"x\")")]
    [InlineData("(substr s 0 5)")]
    [InlineData("(substr s 5)")]
    [InlineData("(replace s \"a\" \"b\")")]
    [InlineData("(upper s)")]
    [InlineData("(lower s)")]
    [InlineData("(trim s)")]
    [InlineData("(ltrim s)")]
    [InlineData("(rtrim s)")]
    [InlineData("(lpad s 10)")]
    [InlineData("(rpad s 10)")]
    [InlineData("(isempty s)")]
    [InlineData("(isblank s)")]
    [InlineData("(split s \",\")")]
    [InlineData("(str s)")]
    public void RoundTrip_StringOp_ProducesValidCalor(string op)
    {
        var source = WrapInFunction($"§R {op}");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var calorEmitter = new CalorEmitter();
        var roundTripped = calorEmitter.Emit(module);

        Assert.Contains(op, roundTripped);
    }

    #endregion

    #region Phase 7: E2E Scenario Tests

    [Fact]
    public void E2E_EmailDomainExtractor()
    {
        // Extract domain from email: get part after @
        var source = """
            §M{m001:Test}
            §F{f001:GetDomain:pub}
              §I{string:email}
              §O{string}
              §B{atIdx} (indexof email "@")
              §R (substr email (+ atIdx 1))
            §/F{f001}
            §/M{m001}
            """;
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("email.IndexOf(\"@\")", code);
        Assert.Contains("email.Substring((atIdx + 1))", code);
    }

    [Fact]
    public void E2E_SlugGenerator()
    {
        // Generate slug: lowercase, replace spaces with dashes
        var source = """
            §M{m001:Test}
            §F{f001:ToSlug:pub}
              §I{string:title}
              §O{string}
              §B{cleaned} (lower (trim title))
              §R (replace cleaned " " "-")
            §/F{f001}
            §/M{m001}
            """;
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("title.Trim().ToLower()", code);
        Assert.Contains(".Replace(\" \", \"-\")", code);
    }

    [Fact]
    public void E2E_StringValidator()
    {
        // Validate non-empty and starts with prefix
        var source = """
            §M{m001:Test}
            §F{f001:IsValid:pub}
              §I{string:value}
              §I{string:prefix}
              §O{bool}
              §R (&& (! (isempty value)) (starts value prefix))
            §/F{f001}
            §/M{m001}
            """;
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("string.IsNullOrEmpty(value)", code);
        Assert.Contains("value.StartsWith(prefix)", code);
    }

    #endregion

    #region Phase 8: StringOpExtensions Tests

    [Theory]
    [InlineData("len", StringOp.Length)]
    [InlineData("contains", StringOp.Contains)]
    [InlineData("starts", StringOp.StartsWith)]
    [InlineData("ends", StringOp.EndsWith)]
    [InlineData("indexof", StringOp.IndexOf)]
    [InlineData("substr", StringOp.Substring)]
    [InlineData("replace", StringOp.Replace)]
    [InlineData("upper", StringOp.ToUpper)]
    [InlineData("lower", StringOp.ToLower)]
    [InlineData("trim", StringOp.Trim)]
    [InlineData("ltrim", StringOp.TrimStart)]
    [InlineData("rtrim", StringOp.TrimEnd)]
    [InlineData("lpad", StringOp.PadLeft)]
    [InlineData("rpad", StringOp.PadRight)]
    [InlineData("join", StringOp.Join)]
    [InlineData("fmt", StringOp.Format)]
    [InlineData("concat", StringOp.Concat)]
    [InlineData("isempty", StringOp.IsNullOrEmpty)]
    [InlineData("isblank", StringOp.IsNullOrWhiteSpace)]
    [InlineData("split", StringOp.Split)]
    [InlineData("str", StringOp.ToString)]
    public void FromString_ValidOperator_ReturnsCorrectEnum(string name, StringOp expected)
    {
        var result = StringOpExtensions.FromString(name);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FromString_InvalidOperator_ReturnsNull()
    {
        var result = StringOpExtensions.FromString("invalid");
        Assert.Null(result);
    }

    [Theory]
    [InlineData(StringOp.Length, "len")]
    [InlineData(StringOp.Contains, "contains")]
    [InlineData(StringOp.ToUpper, "upper")]
    [InlineData(StringOp.ToLower, "lower")]
    [InlineData(StringOp.Trim, "trim")]
    [InlineData(StringOp.IsNullOrEmpty, "isempty")]
    [InlineData(StringOp.IsNullOrWhiteSpace, "isblank")]
    public void ToCalorName_ReturnsCorrectName(StringOp op, string expected)
    {
        var result = op.ToCalorName();
        Assert.Equal(expected, result);
    }

    #endregion

    #region Phase 9: C# Migration Tests (CSharpToCalorConverter)

    private static StringOperationNode? FindStringOperationInResult(ConversionResult result)
    {
        if (!result.Success || result.Ast == null) return null;

        // Try functions first
        var func = result.Ast.Functions.FirstOrDefault();
        if (func != null)
        {
            var returnStmt = func.Body.FirstOrDefault() as ReturnStatementNode;
            if (returnStmt?.Expression is StringOperationNode strOp)
                return strOp;
        }

        // Try classes with methods
        foreach (var cls in result.Ast.Classes)
        {
            foreach (var method in cls.Methods)
            {
                var returnStmt = method.Body.FirstOrDefault() as ReturnStatementNode;
                if (returnStmt?.Expression is StringOperationNode strOp)
                    return strOp;
            }
        }

        return null;
    }

    [Theory]
    [InlineData("s.ToUpper()", StringOp.ToUpper)]
    [InlineData("s.ToLower()", StringOp.ToLower)]
    [InlineData("s.Trim()", StringOp.Trim)]
    [InlineData("s.TrimStart()", StringOp.TrimStart)]
    [InlineData("s.TrimEnd()", StringOp.TrimEnd)]
    public void Migration_InstanceMethodNoArgs_ConvertsToStringOp(string csharpExpr, StringOp expectedOp)
    {
        var csharp = $"public class Test {{ public string M(string s) => {csharpExpr}; }}";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));
        var strOp = FindStringOperationInResult(result);
        Assert.NotNull(strOp);
        Assert.Equal(expectedOp, strOp!.Operation);
    }

    [Theory]
    [InlineData("s.Contains(\"x\")", StringOp.Contains)]
    [InlineData("s.StartsWith(\"x\")", StringOp.StartsWith)]
    [InlineData("s.EndsWith(\"x\")", StringOp.EndsWith)]
    [InlineData("s.IndexOf(\"x\")", StringOp.IndexOf)]
    public void Migration_InstanceMethodOneArg_ConvertsToStringOp(string csharpExpr, StringOp expectedOp)
    {
        var csharp = $"public class Test {{ public object M(string s) => {csharpExpr}; }}";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));
        var strOp = FindStringOperationInResult(result);
        Assert.NotNull(strOp);
        Assert.Equal(expectedOp, strOp!.Operation);
    }

    [Theory]
    [InlineData("s.Substring(5)", StringOp.SubstringFrom)]
    [InlineData("s.PadLeft(10)", StringOp.PadLeft)]
    [InlineData("s.PadRight(10)", StringOp.PadRight)]
    public void Migration_InstanceMethodIntArg_ConvertsToStringOp(string csharpExpr, StringOp expectedOp)
    {
        var csharp = $"public class Test {{ public string M(string s) => {csharpExpr}; }}";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));
        var strOp = FindStringOperationInResult(result);
        Assert.NotNull(strOp);
        Assert.Equal(expectedOp, strOp!.Operation);
    }

    [Theory]
    [InlineData("s.Substring(0, 5)", StringOp.Substring)]
    [InlineData("s.Replace(\"a\", \"b\")", StringOp.Replace)]
    public void Migration_InstanceMethodTwoArgs_ConvertsToStringOp(string csharpExpr, StringOp expectedOp)
    {
        var csharp = $"public class Test {{ public string M(string s) => {csharpExpr}; }}";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));
        var strOp = FindStringOperationInResult(result);
        Assert.NotNull(strOp);
        Assert.Equal(expectedOp, strOp!.Operation);
    }

    [Theory]
    [InlineData("string.IsNullOrEmpty(s)", StringOp.IsNullOrEmpty)]
    [InlineData("string.IsNullOrWhiteSpace(s)", StringOp.IsNullOrWhiteSpace)]
    public void Migration_StaticStringMethod_ConvertsToStringOp(string csharpExpr, StringOp expectedOp)
    {
        var csharp = $"public class Test {{ public bool M(string s) => {csharpExpr}; }}";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));
        var strOp = FindStringOperationInResult(result);
        Assert.NotNull(strOp);
        Assert.Equal(expectedOp, strOp!.Operation);
    }

    [Fact]
    public void Migration_StringLength_ConvertsToLenOp()
    {
        var csharp = "public class Test { public int M(string s) => s.Length; }";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));
        var strOp = FindStringOperationInResult(result);
        Assert.NotNull(strOp);
        Assert.Equal(StringOp.Length, strOp!.Operation);
    }

    [Fact]
    public void Migration_StringConcat_ConvertsToStringOp()
    {
        var csharp = "public class Test { public string M(string a, string b) => string.Concat(a, b); }";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));
        var strOp = FindStringOperationInResult(result);
        Assert.NotNull(strOp);
        Assert.Equal(StringOp.Concat, strOp!.Operation);
    }

    [Fact]
    public void Migration_StringJoin_ConvertsToStringOp()
    {
        var csharp = "public class Test { public string M(string[] items) => string.Join(\",\", items); }";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));
        var strOp = FindStringOperationInResult(result);
        Assert.NotNull(strOp);
        Assert.Equal(StringOp.Join, strOp!.Operation);
    }

    [Fact]
    public void Migration_StringFormat_ConvertsToStringOp()
    {
        var csharp = "public class Test { public string M(string name) => string.Format(\"Hello {0}\", name); }";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));
        var strOp = FindStringOperationInResult(result);
        Assert.NotNull(strOp);
        Assert.Equal(StringOp.Format, strOp!.Operation);
    }

    #endregion

    #region Phase 10: Token Economics Tests

    [Fact]
    public void TokenEconomics_Upper_BuiltinMoreCompact()
    {
        // Builtin: (upper s) = 3 tokens: OpenParen, Identifier, Identifier, CloseParen
        // But in source form, "(upper s)" is much shorter than interop equivalent
        var builtin = "(upper s)";
        var interop = "§C{(. s ToUpper)} §/C"; // Approximate interop syntax

        Assert.True(builtin.Length < interop.Length,
            $"Builtin ({builtin.Length} chars) should be shorter than interop ({interop.Length} chars)");
    }

    [Fact]
    public void TokenEconomics_Contains_BuiltinMoreCompact()
    {
        var builtin = "(contains s \"x\")";
        var interop = "§C{(. s Contains)} §A \"x\" §/C";

        Assert.True(builtin.Length < interop.Length,
            $"Builtin ({builtin.Length} chars) should be shorter than interop ({interop.Length} chars)");
    }

    [Fact]
    public void TokenEconomics_Substring_BuiltinMoreCompact()
    {
        var builtin = "(substr s 0 5)";
        var interop = "§C{(. s Substring)} §A 0 §A 5 §/C";

        Assert.True(builtin.Length < interop.Length,
            $"Builtin ({builtin.Length} chars) should be shorter than interop ({interop.Length} chars)");
    }

    [Fact]
    public void TokenEconomics_IsNullOrEmpty_BuiltinMoreCompact()
    {
        var builtin = "(isempty s)";
        var interop = "§C{(string.IsNullOrEmpty)} §A s §/C";

        Assert.True(builtin.Length < interop.Length,
            $"Builtin ({builtin.Length} chars) should be shorter than interop ({interop.Length} chars)");
    }

    [Fact]
    public void TokenEconomics_Replace_BuiltinMoreCompact()
    {
        var builtin = "(replace s \"a\" \"b\")";
        var interop = "§C{(. s Replace)} §A \"a\" §A \"b\" §/C";

        Assert.True(builtin.Length < interop.Length,
            $"Builtin ({builtin.Length} chars) should be shorter than interop ({interop.Length} chars)");
    }

    #endregion

    #region Phase 11: Additional AST/Emission Coverage

    [Theory]
    [InlineData(StringOp.Length, 1, 1)]
    [InlineData(StringOp.ToUpper, 1, 1)]
    [InlineData(StringOp.ToLower, 1, 1)]
    [InlineData(StringOp.Trim, 1, 1)]
    [InlineData(StringOp.TrimStart, 1, 1)]
    [InlineData(StringOp.TrimEnd, 1, 1)]
    [InlineData(StringOp.IsNullOrEmpty, 1, 1)]
    [InlineData(StringOp.IsNullOrWhiteSpace, 1, 1)]
    [InlineData(StringOp.ToString, 1, 1)]
    [InlineData(StringOp.Contains, 2, 2)]
    [InlineData(StringOp.StartsWith, 2, 2)]
    [InlineData(StringOp.EndsWith, 2, 2)]
    [InlineData(StringOp.IndexOf, 2, 2)]
    [InlineData(StringOp.SubstringFrom, 2, 2)]
    [InlineData(StringOp.Split, 2, 2)]
    [InlineData(StringOp.Join, 2, 2)]
    [InlineData(StringOp.PadLeft, 2, 3)]
    [InlineData(StringOp.PadRight, 2, 3)]
    [InlineData(StringOp.Substring, 3, 3)]
    [InlineData(StringOp.Replace, 3, 3)]
    [InlineData(StringOp.Concat, 2, int.MaxValue)]
    [InlineData(StringOp.Format, 2, int.MaxValue)]
    public void ArgCount_AllOperations_HaveCorrectBounds(StringOp op, int expectedMin, int expectedMax)
    {
        Assert.Equal(expectedMin, op.GetMinArgCount());
        Assert.Equal(expectedMax, op.GetMaxArgCount());
    }

    [Theory]
    [InlineData(StringOp.Substring, "substr")]
    [InlineData(StringOp.SubstringFrom, "substr")]
    [InlineData(StringOp.StartsWith, "starts")]
    [InlineData(StringOp.EndsWith, "ends")]
    [InlineData(StringOp.TrimStart, "ltrim")]
    [InlineData(StringOp.TrimEnd, "rtrim")]
    [InlineData(StringOp.PadLeft, "lpad")]
    [InlineData(StringOp.PadRight, "rpad")]
    [InlineData(StringOp.Format, "fmt")]
    [InlineData(StringOp.ToString, "str")]
    [InlineData(StringOp.Join, "join")]
    [InlineData(StringOp.Concat, "concat")]
    [InlineData(StringOp.Split, "split")]
    [InlineData(StringOp.IndexOf, "indexof")]
    [InlineData(StringOp.Replace, "replace")]
    public void ToCalorName_AllOperations_ReturnValidNames(StringOp op, string expectedName)
    {
        var result = op.ToCalorName();
        Assert.Equal(expectedName, result);
    }

    // Note: Tests for PadLeft/PadRight with char argument removed
    // because Calor doesn't support single-quoted char literals ('0')
    // The functionality works - we just can't test it with the current lexer

    [Fact]
    public void Parse_FormatMultipleArgs_ReturnsCorrectNode()
    {
        var source = WrapInFunctionWithParams("§R (fmt \"{0} + {1} = {2}\" a b c)", "§I{i32:a} §I{i32:b} §I{i32:c}");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.Format, strOp.Operation);
        Assert.Equal(4, strOp.Arguments.Count); // format string + 3 args
    }

    [Fact]
    public void Parse_ConcatMany_ReturnsCorrectNode()
    {
        var source = WrapInFunctionWithParams("§R (concat a b c d e)", "§I{string:a} §I{string:b} §I{string:c} §I{string:d} §I{string:e}");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.Concat, strOp.Operation);
        Assert.Equal(5, strOp.Arguments.Count);
    }

    [Fact]
    public void Emit_ConcatMany_ProducesCorrectCSharp()
    {
        var source = WrapInFunctionWithParams("§R (concat a b c d e)", "§I{string:a} §I{string:b} §I{string:c} §I{string:d} §I{string:e}");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("string.Concat(a, b, c, d, e)", code);
    }

    #endregion
}
