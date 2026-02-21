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
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static string WrapInFunction(string body, string inputType = "string", string outputType = "object")
    {
        return $$"""
            §M{m001:Test}
            §F{f001:Main:pub}
              §I{{{inputType}}:x}
              §O{{{outputType}}}
              {{body}}
            §/F{f001}
            §/M{m001}
            """;
    }

    #region Calor0110 — OperatorArgumentCount

    [Fact]
    public void NullCoalescing_WrongOperandCount_ProducesCalor0110()
    {
        var source = WrapInFunction("§R (?? a b c)", inputType: "object");
        Parse(source, out var diagnostics);

        var error = Assert.Single(diagnostics.Errors, d => d.Code == DiagnosticCode.OperatorArgumentCount);
        Assert.Contains("??", error.Message);
    }

    [Fact]
    public void BinaryOperator_OneOperand_ProducesCalor0110()
    {
        var source = WrapInFunction("§R (+ a)", inputType: "int", outputType: "int");
        Parse(source, out var diagnostics);

        var error = Assert.Single(diagnostics.Errors, d => d.Code == DiagnosticCode.OperatorArgumentCount);
        Assert.Contains("Binary operator", error.Message);
    }

    [Fact]
    public void StringOp_TooFewArgs_ProducesCalor0110()
    {
        // contains requires 2 args (string + substring)
        var source = WrapInFunction("§R (contains x)");
        Parse(source, out var diagnostics);

        var error = Assert.Single(diagnostics.Errors, d => d.Code == DiagnosticCode.OperatorArgumentCount);
        Assert.Contains("String operation", error.Message);
    }

    [Fact]
    public void CharOp_TooFewArgs_ProducesCalor0110()
    {
        // char-at requires 2 args (string + index)
        var source = WrapInFunction("§R (char-at x)");
        Parse(source, out var diagnostics);

        var error = Assert.Single(diagnostics.Errors, d => d.Code == DiagnosticCode.OperatorArgumentCount);
        Assert.Contains("Char operation", error.Message);
    }

    [Fact]
    public void StringBuilderOp_TooFewArgs_ProducesCalor0110()
    {
        // sb-append requires 2 args
        var source = WrapInFunction("§R (sb-append x)", inputType: "StringBuilder", outputType: "object");
        Parse(source, out var diagnostics);

        var error = Assert.Single(diagnostics.Errors, d => d.Code == DiagnosticCode.OperatorArgumentCount);
        Assert.Contains("StringBuilder operation", error.Message);
    }

    [Fact]
    public void TypeOp_WrongArgCount_ProducesCalor0110()
    {
        // cast requires exactly 2 args
        var source = WrapInFunction("§R (cast x)", inputType: "object");
        Parse(source, out var diagnostics);

        var error = Assert.Single(diagnostics.Errors, d => d.Code == DiagnosticCode.OperatorArgumentCount);
        Assert.Contains("Type operation", error.Message);
    }

    [Fact]
    public void StringOp_TooManyArgs_ProducesCalor0110()
    {
        // contains accepts at most 2 args
        var source = WrapInFunction("""§R (contains x "a" "b" "c")""");
        Parse(source, out var diagnostics);

        var error = Assert.Single(diagnostics.Errors, d => d.Code == DiagnosticCode.OperatorArgumentCount);
        Assert.Contains("at most", error.Message);
    }

    [Fact]
    public void CharOp_TooManyArgs_ProducesCalor0110()
    {
        // char-at accepts at most 2 args
        var source = WrapInFunction("§R (char-at x 0 1)");
        Parse(source, out var diagnostics);

        var error = Assert.Single(diagnostics.Errors, d => d.Code == DiagnosticCode.OperatorArgumentCount);
        Assert.Contains("at most", error.Message);
    }

    [Fact]
    public void StringBuilderOp_TooManyArgs_ProducesCalor0110()
    {
        // sb-append accepts at most 2 args
        var source = WrapInFunction("""§R (sb-append x "a" "b")""", inputType: "StringBuilder", outputType: "object");
        Parse(source, out var diagnostics);

        var error = Assert.Single(diagnostics.Errors, d => d.Code == DiagnosticCode.OperatorArgumentCount);
        Assert.Contains("at most", error.Message);
    }

    #endregion

    #region Calor0111 — InvalidComparisonMode

    [Fact]
    public void UnknownComparisonMode_ProducesCalor0111()
    {
        var source = WrapInFunction("§R (contains x \"a\" :bogus)");
        Parse(source, out var diagnostics);

        var error = Assert.Single(diagnostics.Errors, d => d.Code == DiagnosticCode.InvalidComparisonMode);
        Assert.Contains("Unknown comparison mode", error.Message);
    }

    [Fact]
    public void UnsupportedComparisonMode_ProducesCalor0111()
    {
        // len does not support comparison modes
        var source = WrapInFunction("§R (len x :ordinal)");
        Parse(source, out var diagnostics);

        var error = Assert.Single(diagnostics.Errors, d => d.Code == DiagnosticCode.InvalidComparisonMode);
        Assert.Contains("does not support comparison modes", error.Message);
    }

    #endregion

    #region Calor0112 — InvalidCharLiteral

    [Fact]
    public void CharLit_MultiCharString_ProducesCalor0112()
    {
        var source = WrapInFunction("""§R (char-lit "AB")""");
        Parse(source, out var diagnostics);

        var error = Assert.Single(diagnostics.Errors, d => d.Code == DiagnosticCode.InvalidCharLiteral);
        Assert.Contains("single character", error.Message);
    }

    [Fact]
    public void CharLit_NonStringArg_ProducesCalor0112()
    {
        var source = WrapInFunction("§R (char-lit 42)", inputType: "int");
        Parse(source, out var diagnostics);

        var error = Assert.Single(diagnostics.Errors, d => d.Code == DiagnosticCode.InvalidCharLiteral);
        Assert.Contains("string literal argument", error.Message);
    }

    #endregion

    #region Calor0113 — ExpectedTypeName

    [Fact]
    public void TypeOp_NonIdentifierTypeArg_ProducesCalor0113()
    {
        // (cast 42 x) — 42 is not a type name
        var source = WrapInFunction("§R (cast 42 x)", inputType: "object");
        Parse(source, out var diagnostics);

        var error = Assert.Single(diagnostics.Errors, d => d.Code == DiagnosticCode.ExpectedTypeName);
        Assert.Contains("Expected a type name", error.Message);
    }

    [Fact]
    public void TypeOf_NonIdentifierArg_ProducesCalor0113()
    {
        // (typeof 42) — 42 is not a type name, triggers ParseLispTypeName path
        var source = WrapInFunction("§R (typeof 42)", inputType: "object");
        Parse(source, out var diagnostics);

        Assert.Contains(diagnostics.Errors, d => d.Code == DiagnosticCode.ExpectedTypeName);
    }

    #endregion

    #region Calor0114 — InvalidLispExpression

    [Fact]
    public void InvalidOperatorInLispExpression_ProducesCalor0114()
    {
        // Use a literal where an operator is expected
        var source = WrapInFunction("§R (42 x)", inputType: "int", outputType: "int");
        Parse(source, out var diagnostics);

        var error = Assert.Single(diagnostics.Errors, d => d.Code == DiagnosticCode.InvalidLispExpression);
        Assert.Contains("Expected operator in Lisp expression", error.Message);
    }

    [Fact]
    public void StandaloneColon_ProducesCalor0114()
    {
        // A colon followed by a non-identifier (close paren) inside a lisp expression
        var source = WrapInFunction("§R (+ 1 :)");
        Parse(source, out var diagnostics);

        Assert.Contains(diagnostics.Errors, d => d.Code == DiagnosticCode.InvalidLispExpression);
    }

    [Fact]
    public void OperatorInValuePosition_ProducesCalor0114()
    {
        // An operator token (==) where a value argument is expected
        var source = WrapInFunction("§R (+ 1 ==)");
        Parse(source, out var diagnostics);

        Assert.Contains(diagnostics.Errors, d => d.Code == DiagnosticCode.InvalidLispExpression);
    }

    #endregion

    #region Calor0115 — TypeParameterNotFound

    [Fact]
    public void WhereClause_UnknownTypeParam_ProducesCalor0115()
    {
        var source = """
            §M{m001:Test}
            §CL{c001:MyClass:pub}<T>
              §WHERE U : class
              §FLD{T:_item:pri}
            §/CL{c001}
            §/M{m001}
            """;
        Parse(source, out var diagnostics);

        var error = Assert.Single(diagnostics.Errors, d => d.Code == DiagnosticCode.TypeParameterNotFound);
        Assert.Contains("U", error.Message);
        Assert.Contains("not found", error.Message);
    }

    #endregion

    #region Calor0104 — ExpectedExpression (reclassified from hardcoded "Calor0100")

    [Fact]
    public void IfExpression_MissingArrow_ProducesCalor0104()
    {
        // IF condition without → should produce ExpectedExpression
        var source = WrapInFunction("§R §IF{i001} true §/I{i001}", inputType: "bool", outputType: "int");
        Parse(source, out var diagnostics);

        Assert.Contains(diagnostics.Errors, d => d.Code == DiagnosticCode.ExpectedExpression);
    }

    #endregion

    #region Verify Calor0100 is NOT produced for reclassified errors

    [Fact]
    public void OperatorArgCountErrors_DoNotProduceCalor0100()
    {
        var source = WrapInFunction("§R (?? a b c)", inputType: "object");
        Parse(source, out var diagnostics);

        Assert.DoesNotContain(diagnostics.Errors, d => d.Code == DiagnosticCode.UnexpectedToken);
    }

    [Fact]
    public void ComparisonModeErrors_DoNotProduceCalor0100()
    {
        var source = WrapInFunction("§R (contains x \"a\" :bogus)");
        Parse(source, out var diagnostics);

        Assert.DoesNotContain(diagnostics.Errors, d => d.Code == DiagnosticCode.UnexpectedToken);
    }

    [Fact]
    public void CharLitErrors_DoNotProduceCalor0100()
    {
        var source = WrapInFunction("""§R (char-lit "AB")""");
        Parse(source, out var diagnostics);

        Assert.DoesNotContain(diagnostics.Errors, d => d.Code == DiagnosticCode.UnexpectedToken);
    }

    #endregion
}
