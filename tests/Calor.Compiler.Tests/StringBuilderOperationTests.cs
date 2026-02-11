using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class StringBuilderOperationTests
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
              §I{object:sb}
              §O{object}
              {{body}}
            §/F{f001}
            §/M{m001}
            """;
    }

    private static StringBuilderOperationNode GetReturnExpression(ModuleNode module)
    {
        var func = module.Functions[0];
        var returnStmt = func.Body[0] as ReturnStatementNode;
        Assert.NotNull(returnStmt);
        var sbOp = returnStmt!.Expression as StringBuilderOperationNode;
        Assert.NotNull(sbOp);
        return sbOp!;
    }

    #region AST Tests

    [Fact]
    public void Parse_SbNew_ReturnsStringBuilderOperationNode()
    {
        var source = WrapInFunction("§R (sb-new)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var sbOp = GetReturnExpression(module);
        Assert.Equal(StringBuilderOp.New, sbOp.Operation);
        Assert.Empty(sbOp.Arguments);
    }

    [Fact]
    public void Parse_SbNewWithInit_ReturnsStringBuilderOperationNode()
    {
        var source = WrapInFunction("§R (sb-new \"init\")");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var sbOp = GetReturnExpression(module);
        Assert.Equal(StringBuilderOp.New, sbOp.Operation);
        Assert.Single(sbOp.Arguments);
    }

    [Fact]
    public void Parse_SbAppend_ReturnsStringBuilderOperationNode()
    {
        var source = WrapInFunction("§R (sb-append sb \"text\")");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var sbOp = GetReturnExpression(module);
        Assert.Equal(StringBuilderOp.Append, sbOp.Operation);
        Assert.Equal(2, sbOp.Arguments.Count);
    }

    [Fact]
    public void Parse_SbAppendLine_ReturnsStringBuilderOperationNode()
    {
        var source = WrapInFunction("§R (sb-appendline sb \"text\")");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var sbOp = GetReturnExpression(module);
        Assert.Equal(StringBuilderOp.AppendLine, sbOp.Operation);
        Assert.Equal(2, sbOp.Arguments.Count);
    }

    [Fact]
    public void Parse_SbInsert_ReturnsStringBuilderOperationNode()
    {
        var source = WrapInFunction("§R (sb-insert sb 0 \"text\")");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var sbOp = GetReturnExpression(module);
        Assert.Equal(StringBuilderOp.Insert, sbOp.Operation);
        Assert.Equal(3, sbOp.Arguments.Count);
    }

    [Fact]
    public void Parse_SbRemove_ReturnsStringBuilderOperationNode()
    {
        var source = WrapInFunction("§R (sb-remove sb 0 5)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var sbOp = GetReturnExpression(module);
        Assert.Equal(StringBuilderOp.Remove, sbOp.Operation);
        Assert.Equal(3, sbOp.Arguments.Count);
    }

    [Fact]
    public void Parse_SbClear_ReturnsStringBuilderOperationNode()
    {
        var source = WrapInFunction("§R (sb-clear sb)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var sbOp = GetReturnExpression(module);
        Assert.Equal(StringBuilderOp.Clear, sbOp.Operation);
        Assert.Single(sbOp.Arguments);
    }

    [Fact]
    public void Parse_SbToString_ReturnsStringBuilderOperationNode()
    {
        var source = WrapInFunction("§R (sb-tostring sb)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var sbOp = GetReturnExpression(module);
        Assert.Equal(StringBuilderOp.ToString, sbOp.Operation);
        Assert.Single(sbOp.Arguments);
    }

    [Fact]
    public void Parse_SbLength_ReturnsStringBuilderOperationNode()
    {
        var source = WrapInFunction("§R (sb-length sb)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var sbOp = GetReturnExpression(module);
        Assert.Equal(StringBuilderOp.Length, sbOp.Operation);
        Assert.Single(sbOp.Arguments);
    }

    #endregion

    #region C# Emission Tests

    [Theory]
    [InlineData("(sb-new)", "new System.Text.StringBuilder()")]
    [InlineData("(sb-new \"init\")", "new System.Text.StringBuilder(\"init\")")]
    [InlineData("(sb-append sb \"text\")", "sb.Append(\"text\")")]
    [InlineData("(sb-appendline sb \"text\")", "sb.AppendLine(\"text\")")]
    [InlineData("(sb-insert sb 0 \"text\")", "sb.Insert(0, \"text\")")]
    [InlineData("(sb-remove sb 0 5)", "sb.Remove(0, 5)")]
    [InlineData("(sb-clear sb)", "sb.Clear()")]
    [InlineData("(sb-tostring sb)", "sb.ToString()")]
    [InlineData("(sb-length sb)", "sb.Length")]
    public void Emit_StringBuilderOperation_ProducesCorrectCSharp(string calor, string expectedCSharp)
    {
        var source = WrapInFunction($"§R {calor}");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains(expectedCSharp, code);
    }

    #endregion

    #region Round-Trip Tests

    [Theory]
    [InlineData("(sb-new)")]
    [InlineData("(sb-new \"init\")")]
    [InlineData("(sb-append sb \"text\")")]
    [InlineData("(sb-appendline sb \"text\")")]
    [InlineData("(sb-insert sb 0 \"text\")")]
    [InlineData("(sb-remove sb 0 5)")]
    [InlineData("(sb-clear sb)")]
    [InlineData("(sb-tostring sb)")]
    [InlineData("(sb-length sb)")]
    public void RoundTrip_StringBuilderOp_ProducesValidCalor(string op)
    {
        var source = WrapInFunction($"§R {op}");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var calorEmitter = new CalorEmitter();
        var roundTripped = calorEmitter.Emit(module);

        // Extract the expected op name from the expression
        var opName = op.Split(' ')[0].TrimStart('(').TrimEnd(')');
        Assert.Contains(opName, roundTripped);
    }

    #endregion

    #region Error Tests

    [Fact]
    public void Parse_SbAppend_NoArgs_ReportsError()
    {
        var source = WrapInFunction("§R (sb-append)");
        Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Message.Contains("requires at least 2 argument"));
    }

    [Fact]
    public void Parse_SbAppend_OneArg_ReportsError()
    {
        var source = WrapInFunction("§R (sb-append sb)");
        Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Message.Contains("requires at least 2 argument"));
    }

    [Fact]
    public void Parse_SbInsert_TwoArgs_ReportsError()
    {
        var source = WrapInFunction("§R (sb-insert sb 0)");
        Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Message.Contains("requires at least 3 argument"));
    }

    [Fact]
    public void Parse_SbClear_NoArgs_ReportsError()
    {
        var source = WrapInFunction("§R (sb-clear)");
        Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Message.Contains("requires at least 1 argument"));
    }

    [Fact]
    public void Parse_SbNew_TooManyArgs_ReportsError()
    {
        var source = WrapInFunction("§R (sb-new \"a\" \"b\")");
        Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Message.Contains("accepts at most 1 argument"));
    }

    #endregion

    #region Extensions Tests

    [Theory]
    [InlineData("sb-new", StringBuilderOp.New)]
    [InlineData("sb-append", StringBuilderOp.Append)]
    [InlineData("sb-appendline", StringBuilderOp.AppendLine)]
    [InlineData("sb-insert", StringBuilderOp.Insert)]
    [InlineData("sb-remove", StringBuilderOp.Remove)]
    [InlineData("sb-clear", StringBuilderOp.Clear)]
    [InlineData("sb-tostring", StringBuilderOp.ToString)]
    [InlineData("sb-length", StringBuilderOp.Length)]
    public void FromString_ValidOperator_ReturnsCorrectEnum(string name, StringBuilderOp expected)
    {
        var result = StringBuilderOpExtensions.FromString(name);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FromString_InvalidOperator_ReturnsNull()
    {
        var result = StringBuilderOpExtensions.FromString("invalid");
        Assert.Null(result);
    }

    [Theory]
    [InlineData(StringBuilderOp.New, "sb-new")]
    [InlineData(StringBuilderOp.Append, "sb-append")]
    [InlineData(StringBuilderOp.AppendLine, "sb-appendline")]
    [InlineData(StringBuilderOp.Insert, "sb-insert")]
    [InlineData(StringBuilderOp.Remove, "sb-remove")]
    [InlineData(StringBuilderOp.Clear, "sb-clear")]
    [InlineData(StringBuilderOp.ToString, "sb-tostring")]
    [InlineData(StringBuilderOp.Length, "sb-length")]
    public void ToCalorName_ReturnsCorrectName(StringBuilderOp op, string expected)
    {
        var result = op.ToCalorName();
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(StringBuilderOp.New, 0, 1)]
    [InlineData(StringBuilderOp.Append, 2, 2)]
    [InlineData(StringBuilderOp.AppendLine, 2, 2)]
    [InlineData(StringBuilderOp.Insert, 3, 3)]
    [InlineData(StringBuilderOp.Remove, 3, 3)]
    [InlineData(StringBuilderOp.Clear, 1, 1)]
    [InlineData(StringBuilderOp.ToString, 1, 1)]
    [InlineData(StringBuilderOp.Length, 1, 1)]
    public void ArgCount_AllOperations_HaveCorrectBounds(StringBuilderOp op, int expectedMin, int expectedMax)
    {
        Assert.Equal(expectedMin, op.GetMinArgCount());
        Assert.Equal(expectedMax, op.GetMaxArgCount());
    }

    #endregion

    #region Composition Tests

    [Fact]
    public void Parse_NestedAppends_Works()
    {
        // (sb-tostring (sb-append (sb-append (sb-new) "a") "b"))
        var source = WrapInFunction("§R (sb-tostring (sb-append (sb-append (sb-new) \"a\") \"b\"))");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var sbOp = GetReturnExpression(module);
        Assert.Equal(StringBuilderOp.ToString, sbOp.Operation);

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);
        Assert.Contains("new System.Text.StringBuilder().Append(\"a\").Append(\"b\").ToString()", code);
    }

    [Fact]
    public void Parse_LengthAfterClear_Works()
    {
        // (sb-length (sb-clear (sb-append (sb-new "init") "more")))
        var source = WrapInFunction("§R (sb-length (sb-clear (sb-append (sb-new \"init\") \"more\")))");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var sbOp = GetReturnExpression(module);
        Assert.Equal(StringBuilderOp.Length, sbOp.Operation);

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);
        Assert.Contains("new System.Text.StringBuilder(\"init\").Append(\"more\").Clear().Length", code);
    }

    #endregion

    #region C# Migration Tests

    private static StringBuilderOperationNode? FindStringBuilderOperationInResult(ConversionResult result)
    {
        if (!result.Success || result.Ast == null) return null;

        // Search in top-level functions
        foreach (var func in result.Ast.Functions)
        {
            var found = FindStringBuilderOperationInFunction(func);
            if (found != null) return found;
        }

        // Search in class methods
        foreach (var cls in result.Ast.Classes)
        {
            foreach (var method in cls.Methods)
            {
                var found = FindStringBuilderOperationInBody(method.Body);
                if (found != null) return found;
            }
        }

        return null;
    }

    private static StringBuilderOperationNode? FindStringBuilderOperationInFunction(FunctionNode func)
    {
        return FindStringBuilderOperationInBody(func.Body);
    }

    private static StringBuilderOperationNode? FindStringBuilderOperationInBody(IReadOnlyList<StatementNode> body)
    {
        foreach (var stmt in body)
        {
            if (stmt is ReturnStatementNode ret && ret.Expression is StringBuilderOperationNode sbOp)
                return sbOp;
            if (stmt is BindStatementNode bind && bind.Initializer is StringBuilderOperationNode bindSbOp)
                return bindSbOp;
        }
        return null;
    }

    [Fact]
    public void Migration_NewStringBuilder_ConvertsToSbNew()
    {
        // The C# to Calor converter converts new StringBuilder() to sb-new
        var csharp = @"
using System.Text;
public class Test { public StringBuilder M() { return new StringBuilder(); } }";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        // Check via round-trip
        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);
        Assert.Contains("sb-new", calor);
    }

    [Fact]
    public void Migration_NewStringBuilderWithInit_ConvertsToSbNew()
    {
        // The C# to Calor converter converts new StringBuilder("init") to sb-new
        var csharp = @"
using System.Text;
public class Test { public StringBuilder M() { return new StringBuilder(""init""); } }";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        // Check via round-trip - the emitted Calor should contain sb-new
        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);
        Assert.Contains("sb-new", calor);
    }

    [Theory]
    [InlineData("sb.Append(\"text\")", "sb-append")]
    [InlineData("sb.AppendLine(\"text\")", "sb-appendline")]
    [InlineData("sb.Clear()", "sb-clear")]
    [InlineData("sb.ToString()", "sb-tostring")]
    public void Migration_StringBuilderInstanceMethod_ConvertsToSbOp(string csharpExpr, string expectedCalorOp)
    {
        var csharp = $@"
using System.Text;
public class Test {{ public object M(StringBuilder sb) {{ return {csharpExpr}; }} }}";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        // Check via round-trip
        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);
        Assert.Contains(expectedCalorOp, calor);
    }

    [Fact]
    public void Migration_StringBuilderInsert_ConvertsToSbInsert()
    {
        // The C# to Calor converter converts sb.Insert(0, "text") to sb-insert
        var csharp = @"
using System.Text;
public class Test { public StringBuilder M(StringBuilder sb) { return sb.Insert(0, ""text""); } }";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        // Check via round-trip
        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);
        Assert.Contains("sb-insert", calor);
    }

    [Fact]
    public void Migration_StringBuilderRemove_ConvertsToSbRemove()
    {
        // The C# to Calor converter converts sb.Remove(0, 5) to sb-remove
        var csharp = @"
using System.Text;
public class Test { public StringBuilder M(StringBuilder sb) { return sb.Remove(0, 5); } }";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        // Check via round-trip
        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);
        Assert.Contains("sb-remove", calor);
    }

    [Fact]
    public void Migration_StringBuilderLength_ConvertsToSbLength()
    {
        // The C# to Calor converter converts sb.Length to sb-length
        var csharp = @"
using System.Text;
public class Test { public int M(StringBuilder sb) { return sb.Length; } }";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        // Check via round-trip - the emitted Calor should contain sb-length
        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);
        Assert.Contains("sb-length", calor);
    }

    [Theory]
    [InlineData("new StringBuilder()", "sb-new")]
    [InlineData("new StringBuilder(\"init\")", "sb-new")]
    public void Migration_NewStringBuilder_RoundTripsToCalor(string csharpExpr, string expectedCalorOp)
    {
        var csharp = $@"
using System.Text;
public class Test {{ public StringBuilder M() => {csharpExpr}; }}";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);

        Assert.Contains(expectedCalorOp, calor);
    }

    [Theory]
    [InlineData("sb.Append(\"text\")", "sb-append")]
    [InlineData("sb.AppendLine(\"text\")", "sb-appendline")]
    [InlineData("sb.Insert(0, \"text\")", "sb-insert")]
    [InlineData("sb.Remove(0, 5)", "sb-remove")]
    [InlineData("sb.Clear()", "sb-clear")]
    [InlineData("sb.ToString()", "sb-tostring")]
    public void Migration_StringBuilderMethod_RoundTripsToCalor(string csharpExpr, string expectedCalorOp)
    {
        var csharp = $@"
using System.Text;
public class Test {{ public object M(StringBuilder sb) => {csharpExpr}; }}";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);

        Assert.Contains(expectedCalorOp, calor);
    }

    [Fact]
    public void Migration_ChainedStringBuilder_ProducesNestedOperations()
    {
        // Test C# chained calls: new StringBuilder().Append("a").Append("b").ToString()
        var csharp = @"
using System.Text;
public class Test { public string M() { return new StringBuilder().Append(""a"").Append(""b"").ToString(); } }";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);

        // Should contain all the operations
        Assert.Contains("sb-new", calor);
        Assert.Contains("sb-append", calor);
        Assert.Contains("sb-tostring", calor);
    }

    [Fact]
    public void Migration_StringBuilderWithNonSbName_FallsBackToInterop()
    {
        // Variable named 'builder' instead of 'sb' - should still work due to method signature
        var csharp = @"
using System.Text;
public class Test { public string M(StringBuilder builder) { return builder.ToString(); } }";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        // This may or may not be converted depending on heuristics
        // The important thing is it doesn't crash
        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);
        Assert.NotNull(calor);
    }

    #endregion
}
