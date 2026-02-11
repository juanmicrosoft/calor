using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class RegexOperationTests
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
              §O{object}
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

    #region AST Tests

    [Fact]
    public void Parse_RegexTest_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (regex-test s \"\\\\d+\")");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.RegexTest, strOp.Operation);
        Assert.Equal(2, strOp.Arguments.Count);
    }

    [Fact]
    public void Parse_RegexMatch_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (regex-match s \"\\\\d+\")");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.RegexMatch, strOp.Operation);
        Assert.Equal(2, strOp.Arguments.Count);
    }

    [Fact]
    public void Parse_RegexReplace_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (regex-replace s \"\\\\s+\" \"-\")");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.RegexReplace, strOp.Operation);
        Assert.Equal(3, strOp.Arguments.Count);
    }

    [Fact]
    public void Parse_RegexSplit_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (regex-split s \",\")");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.RegexSplit, strOp.Operation);
        Assert.Equal(2, strOp.Arguments.Count);
    }

    #endregion

    #region C# Emission Tests

    [Fact]
    public void Emit_RegexTest_ProducesCorrectCSharp()
    {
        var source = WrapInFunction("§R (regex-test s \"\\\\d+\")");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        // The emitted C# will have \\d+ because Calor source has \\d+ (which is \d+ in the parsed string)
        Assert.Contains("System.Text.RegularExpressions.Regex.IsMatch(s, \"\\\\d+\")", code);
    }

    [Fact]
    public void Emit_RegexMatch_ProducesCorrectCSharp()
    {
        var source = WrapInFunction("§R (regex-match s \"\\\\d+\")");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("System.Text.RegularExpressions.Regex.Match(s, \"\\\\d+\")", code);
    }

    [Fact]
    public void Emit_RegexReplace_ProducesCorrectCSharp()
    {
        var source = WrapInFunction("§R (regex-replace s \"\\\\s+\" \"-\")");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("System.Text.RegularExpressions.Regex.Replace(s, \"\\\\s+\", \"-\")", code);
    }

    [Fact]
    public void Emit_RegexSplit_ProducesCorrectCSharp()
    {
        var source = WrapInFunction("§R (regex-split s \",\")");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("System.Text.RegularExpressions.Regex.Split(s, \",\")", code);
    }

    #endregion

    #region Round-Trip Tests

    [Theory]
    [InlineData("(regex-test s \"\\\\d+\")")]
    [InlineData("(regex-match s \"\\\\d+\")")]
    [InlineData("(regex-replace s \"\\\\s+\" \"-\")")]
    [InlineData("(regex-split s \",\")")]
    public void RoundTrip_RegexOp_ProducesValidCalor(string op)
    {
        var source = WrapInFunction($"§R {op}");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var calorEmitter = new CalorEmitter();
        var roundTripped = calorEmitter.Emit(module);

        // Extract the expected op name from the expression
        var opName = op.Split(' ')[0].TrimStart('(');
        Assert.Contains(opName, roundTripped);
    }

    #endregion

    #region Error Tests

    [Fact]
    public void Parse_RegexTest_NoArgs_ReportsError()
    {
        var source = WrapInFunction("§R (regex-test)");
        Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Message.Contains("requires at least 2 argument"));
    }

    [Fact]
    public void Parse_RegexTest_OneArg_ReportsError()
    {
        var source = WrapInFunction("§R (regex-test s)");
        Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Message.Contains("requires at least 2 argument"));
    }

    [Fact]
    public void Parse_RegexReplace_TwoArgs_ReportsError()
    {
        var source = WrapInFunction("§R (regex-replace s \"pattern\")");
        Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Message.Contains("requires at least 3 argument"));
    }

    #endregion

    #region Extensions Tests

    [Theory]
    [InlineData("regex-test", StringOp.RegexTest)]
    [InlineData("regex-match", StringOp.RegexMatch)]
    [InlineData("regex-replace", StringOp.RegexReplace)]
    [InlineData("regex-split", StringOp.RegexSplit)]
    public void FromString_ValidOperator_ReturnsCorrectEnum(string name, StringOp expected)
    {
        var result = StringOpExtensions.FromString(name);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(StringOp.RegexTest, "regex-test")]
    [InlineData(StringOp.RegexMatch, "regex-match")]
    [InlineData(StringOp.RegexReplace, "regex-replace")]
    [InlineData(StringOp.RegexSplit, "regex-split")]
    public void ToCalorName_ReturnsCorrectName(StringOp op, string expected)
    {
        var result = op.ToCalorName();
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(StringOp.RegexTest, 2, 2)]
    [InlineData(StringOp.RegexMatch, 2, 2)]
    [InlineData(StringOp.RegexReplace, 3, 3)]
    [InlineData(StringOp.RegexSplit, 2, 2)]
    public void ArgCount_ReturnsCorrectBounds(StringOp op, int expectedMin, int expectedMax)
    {
        Assert.Equal(expectedMin, op.GetMinArgCount());
        Assert.Equal(expectedMax, op.GetMaxArgCount());
    }

    #endregion

    #region C# Migration Tests

    private static StringOperationNode? FindStringOperationInResult(ConversionResult result)
    {
        if (!result.Success || result.Ast == null) return null;

        // Search in top-level functions
        foreach (var func in result.Ast.Functions)
        {
            var found = FindStringOperationInFunction(func);
            if (found != null) return found;
        }

        // Search in class methods
        foreach (var cls in result.Ast.Classes)
        {
            foreach (var method in cls.Methods)
            {
                var found = FindStringOperationInBody(method.Body);
                if (found != null) return found;
            }
        }

        return null;
    }

    private static StringOperationNode? FindStringOperationInFunction(FunctionNode func)
    {
        return FindStringOperationInBody(func.Body);
    }

    private static StringOperationNode? FindStringOperationInBody(IReadOnlyList<StatementNode> body)
    {
        foreach (var stmt in body)
        {
            if (stmt is ReturnStatementNode ret && ret.Expression is StringOperationNode strOp)
                return strOp;
            if (stmt is BindStatementNode bind && bind.Initializer is StringOperationNode bindStrOp)
                return bindStrOp;
        }
        return null;
    }

    [Theory]
    [InlineData("Regex.IsMatch(s, \"\\\\d+\")", "regex-test")]
    [InlineData("Regex.Match(s, \"\\\\d+\")", "regex-match")]
    [InlineData("Regex.Split(s, \",\")", "regex-split")]
    public void Migration_RegexStaticMethod_ConvertsToStringOp(string csharpExpr, string expectedCalorOp)
    {
        // The C# to Calor converter converts Regex static methods
        var csharp = $@"
using System.Text.RegularExpressions;
public class Test {{ public object M(string s) {{ return {csharpExpr}; }} }}";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        // Check via round-trip
        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);
        Assert.Contains(expectedCalorOp, calor);
    }

    [Fact]
    public void Migration_RegexReplace_ConvertsToStringOp()
    {
        // The C# to Calor converter converts Regex.Replace
        var csharp = @"
using System.Text.RegularExpressions;
public class Test { public string M(string s) { return Regex.Replace(s, ""\\s+"", ""-""); } }";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        // Check via round-trip
        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);
        Assert.Contains("regex-replace", calor);
    }

    [Fact]
    public void Migration_RegexIsMatch_FullyQualified_ConvertsToStringOp()
    {
        // The C# to Calor converter converts fully qualified Regex.IsMatch
        var csharp = @"
public class Test { public bool M(string s) { return System.Text.RegularExpressions.Regex.IsMatch(s, ""\\d+""); } }";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        // Check via round-trip
        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);
        Assert.Contains("regex-test", calor);
    }

    [Theory]
    [InlineData("Regex.IsMatch(s, \"\\\\d+\")", "regex-test")]
    [InlineData("Regex.Match(s, \"\\\\d+\")", "regex-match")]
    [InlineData("Regex.Replace(s, \"\\\\s+\", \"-\")", "regex-replace")]
    [InlineData("Regex.Split(s, \",\")", "regex-split")]
    public void Migration_RegexOperation_RoundTripsToCalor(string csharpExpr, string expectedCalorOp)
    {
        var csharp = $@"
using System.Text.RegularExpressions;
public class Test {{ public object M(string s) => {csharpExpr}; }}";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);

        Assert.Contains(expectedCalorOp, calor);
    }

    #endregion
}
