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
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static string WrapInFunction(string body)
    {
        return $$"""
            module Test Test block
              func Main (string:s) → object
                !
                {{body}}
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
    #endregion
    #region C# Emission Tests
    #endregion
    #region Round-Trip Tests
    #endregion
    #region Error Tests

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
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
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
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
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
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
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
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);

        Assert.Contains(expectedCalorOp, calor);
    }

    #endregion
}
