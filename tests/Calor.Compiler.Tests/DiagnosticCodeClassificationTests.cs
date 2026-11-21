using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests verifying that specific parser errors produce the correct diagnostic codes
/// instead of the catch-all Calor0100 (UnexpectedToken).
/// </summary>
public class DiagnosticCodeClassificationTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static string WrapInFunction(string body, string inputType = "string", string outputType = "object")
    {
        return $$"""
            module Test Test block
              func Main ({{inputType}}:x) → {{outputType}}
                !
                {{body}}
            """;
    }
    #region Calor0110 — OperatorArgumentCount
    #endregion
    #region Calor0111 — InvalidComparisonMode
    #endregion
    #region Calor0112 — InvalidCharLiteral
    #endregion
    #region Calor0113 — ExpectedTypeName

    #endregion

    #region Calor0114 — InvalidLispExpression

    [Fact]
    public void GroupingWithoutAnOperator_ProducesCalor0100()
    {
        // Ordinary grouping requires an operator between these two operands.
        var source = WrapInFunction("§R (42 x)", inputType: "int", outputType: "int");
        Parse(source, out var diagnostics);

        Assert.Contains(diagnostics.Errors, d => d.Code == DiagnosticCode.UnexpectedToken
            && d.Message.Contains("Expected CloseParen"));
    }

    #endregion

    #region Calor0115 — TypeParameterNotFound

    [Fact]
    public void WhereClause_UnknownTypeParam_ProducesCalor0115()
    {
        var source = """
            module Test Test block
              class MyClass<T>
                where U : class
                field T:_item private
            """;
        Parse(source, out var diagnostics);

        var error = Assert.Single(diagnostics.Errors, d => d.Code == DiagnosticCode.TypeParameterNotFound);
        Assert.Contains("U", error.Message);
        Assert.Contains("not found", error.Message);
    }
    #endregion
    #region Calor0104 — ExpectedExpression (reclassified from hardcoded "Calor0100")
    #endregion
    #region Verify Calor0100 is NOT produced for reclassified errors

    #endregion
}
