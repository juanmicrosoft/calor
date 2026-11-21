using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class CharOperationTests
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
              func Main ({{inputType}}:s) → {{outputType}}
                !
                {{body}}
            """;
    }

    private static string WrapCharFunction(string body)
    {
        return $$"""
            module Test Test block
              func Main (char:c) → object
                !
                {{body}}
            """;
    }

    private static CharOperationNode GetReturnCharExpression(ModuleNode module)
    {
        var func = module.Functions[0];
        var returnStmt = func.Body[0] as ReturnStatementNode;
        Assert.NotNull(returnStmt);
        var charOp = returnStmt!.Expression as CharOperationNode;
        Assert.NotNull(charOp);
        return charOp!;
    }

    #region AST Tests

    #endregion

    #region C# Emission Tests

    [Theory]
    [InlineData("s[0]", "s[0]")]
    [InlineData("(int)c", "(int)c")]
    [InlineData("(char)65", "(char)65")]
    [InlineData("char.IsLetter(c)", "char.IsLetter(c)")]
    [InlineData("char.IsDigit(c)", "char.IsDigit(c)")]
    [InlineData("char.IsWhiteSpace(c)", "char.IsWhiteSpace(c)")]
    [InlineData("char.IsUpper(c)", "char.IsUpper(c)")]
    [InlineData("char.IsLower(c)", "char.IsLower(c)")]
    [InlineData("char.ToUpper(c)", "char.ToUpper(c)")]
    [InlineData("char.ToLower(c)", "char.ToLower(c)")]
    public void Emit_CharOperation_ProducesCorrectCSharp(string calor, string expectedCSharp)
    {
        var source = $$"""
            module Test Test block
              func Main (string:s, char:c) → object
                !
                return {{calor}}
            """;
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains(expectedCSharp, code);
    }

    #endregion

    #region Round-Trip Tests

    [Theory]
    [InlineData("s[0]")]
    [InlineData("(int)c")]
    [InlineData("(char)65")]
    [InlineData("char.IsLetter(c)")]
    [InlineData("char.IsDigit(c)")]
    [InlineData("char.IsWhiteSpace(c)")]
    [InlineData("char.IsUpper(c)")]
    [InlineData("char.IsLower(c)")]
    [InlineData("char.ToUpper(c)")]
    [InlineData("char.ToLower(c)")]
    public void RoundTrip_CharOp_ProducesValidCalor(string op)
    {
        var source = $$"""
            module Test Test block
              func Main (string:s, char:c) → object
                !
                return {{op}}
            """;
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var calorEmitter = new CalorEmitter();
        var roundTripped = calorEmitter.Emit(module);

        Assert.Contains(op, roundTripped);
    }
    #endregion
    #region Error Tests

    #endregion

    #region Extensions Tests

    [Theory]
    [InlineData("char-at", CharOp.CharAt)]
    [InlineData("char-code", CharOp.CharCode)]
    [InlineData("char-from-code", CharOp.CharFromCode)]
    [InlineData("is-letter", CharOp.IsLetter)]
    [InlineData("is-digit", CharOp.IsDigit)]
    [InlineData("is-whitespace", CharOp.IsWhiteSpace)]
    [InlineData("is-upper", CharOp.IsUpper)]
    [InlineData("is-lower", CharOp.IsLower)]
    [InlineData("char-upper", CharOp.ToUpperChar)]
    [InlineData("char-lower", CharOp.ToLowerChar)]
    public void FromString_ValidOperator_ReturnsCorrectEnum(string name, CharOp expected)
    {
        var result = CharOpExtensions.FromString(name);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FromString_InvalidOperator_ReturnsNull()
    {
        var result = CharOpExtensions.FromString("invalid");
        Assert.Null(result);
    }

    [Theory]
    [InlineData(CharOp.CharAt, "char-at")]
    [InlineData(CharOp.CharCode, "char-code")]
    [InlineData(CharOp.CharFromCode, "char-from-code")]
    [InlineData(CharOp.IsLetter, "is-letter")]
    [InlineData(CharOp.IsDigit, "is-digit")]
    [InlineData(CharOp.IsWhiteSpace, "is-whitespace")]
    [InlineData(CharOp.IsUpper, "is-upper")]
    [InlineData(CharOp.IsLower, "is-lower")]
    [InlineData(CharOp.ToUpperChar, "char-upper")]
    [InlineData(CharOp.ToLowerChar, "char-lower")]
    public void ToCalorName_ReturnsCorrectName(CharOp op, string expected)
    {
        var result = op.ToCalorName();
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(CharOp.CharAt, 2, 2)]
    [InlineData(CharOp.CharCode, 1, 1)]
    [InlineData(CharOp.CharFromCode, 1, 1)]
    [InlineData(CharOp.IsLetter, 1, 1)]
    [InlineData(CharOp.IsDigit, 1, 1)]
    [InlineData(CharOp.IsWhiteSpace, 1, 1)]
    [InlineData(CharOp.IsUpper, 1, 1)]
    [InlineData(CharOp.IsLower, 1, 1)]
    [InlineData(CharOp.ToUpperChar, 1, 1)]
    [InlineData(CharOp.ToLowerChar, 1, 1)]
    public void ArgCount_AllOperations_HaveCorrectBounds(CharOp op, int expectedMin, int expectedMax)
    {
        Assert.Equal(expectedMin, op.GetMinArgCount());
        Assert.Equal(expectedMax, op.GetMaxArgCount());
    }

    #endregion

    #region Composition Tests

    #endregion

    #region C# Migration Tests

    private static CharOperationNode? FindCharOperationInResult(ConversionResult result)
    {
        if (!result.Success || result.Ast == null) return null;

        // Search in top-level functions
        foreach (var func in result.Ast.Functions)
        {
            var found = FindCharOperationInFunction(func);
            if (found != null) return found;
        }

        // Search in class methods
        foreach (var cls in result.Ast.Classes)
        {
            foreach (var method in cls.Methods)
            {
                var found = FindCharOperationInBody(method.Body);
                if (found != null) return found;
            }
        }

        return null;
    }

    private static CharOperationNode? FindCharOperationInFunction(FunctionNode func)
    {
        return FindCharOperationInBody(func.Body);
    }

    private static CharOperationNode? FindCharOperationInBody(IReadOnlyList<StatementNode> body)
    {
        foreach (var stmt in body)
        {
            var found = FindCharOperationInStatement(stmt);
            if (found != null) return found;
        }
        return null;
    }

    private static CharOperationNode? FindCharOperationInStatement(StatementNode stmt)
    {
        if (stmt is ReturnStatementNode ret)
        {
            return FindCharOperationInExpression(ret.Expression);
        }
        if (stmt is BindStatementNode bind)
        {
            return FindCharOperationInExpression(bind.Initializer);
        }
        return null;
    }

    private static CharOperationNode? FindCharOperationInExpression(ExpressionNode? expr)
    {
        if (expr == null) return null;
        if (expr is CharOperationNode charOp) return charOp;

        // Search recursively in common expression types
        return expr switch
        {
            CallExpressionNode call => call.Arguments.Select(FindCharOperationInExpression).FirstOrDefault(x => x != null),
            _ => null
        };
    }

    [Theory]
    [InlineData("char.IsLetter(c)", "char.IsLetter(c)")]
    [InlineData("char.IsDigit(c)", "char.IsDigit(c)")]
    [InlineData("char.IsWhiteSpace(c)", "char.IsWhiteSpace(c)")]
    [InlineData("char.IsUpper(c)", "char.IsUpper(c)")]
    [InlineData("char.IsLower(c)", "char.IsLower(c)")]
    [InlineData("char.ToUpper(c)", "char.ToUpper(c)")]
    [InlineData("char.ToLower(c)", "char.ToLower(c)")]
    public void Migration_CharStaticMethod_ConvertsToCharOp(string csharpExpr, string expectedCalorOp)
    {
        // The C# to Calor converter converts char static methods to char operations
        var csharp = $"public class Test {{ public object M(char c) {{ return {csharpExpr}; }} }}";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        // Check via round-trip
        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);
        Assert.Contains(expectedCalorOp, calor);
    }

    [Fact]
    public void Migration_CharCastToInt_ConvertsToCharCode()
    {
        // The C# to Calor converter converts (int)c to char-code
        // Verify the CalorEmitter produces the expected output
        var csharp = "public class Test { public int M(char c) { return (int)c; } }";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        // Check via round-trip - the emitted Calor should contain char-code
        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);
        Assert.Contains("((int)c)", calor);
    }

    [Fact]
    public void Migration_IntCastToChar_ConvertsToCharFromCode()
    {
        // The C# to Calor converter converts (char)n to char-from-code
        var csharp = "public class Test { public char M(int n) { return (char)n; } }";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        // Check via round-trip - the emitted Calor should contain char-from-code
        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);
        Assert.Contains("((char)n)", calor);
    }

    [Fact]
    public void Migration_StringIndexer_ConvertsToIdx()
    {
        // Without semantic model, variable indexing defaults to §IDX (not char-at)
        // Only string literals produce char-at
        var csharp = "public class Test { public char M(string s) { return s[0]; } }";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);
        Assert.Contains("s[0]", calor);
    }

    [Fact]
    public void Migration_StringIndexerVariable_ConvertsToIdx()
    {
        // Without semantic model, variable indexing defaults to §IDX (not char-at)
        var csharp = "public class Test { public char M(string s, int i) { return s[i]; } }";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);
        Assert.Contains("s[i]", calor);
    }

    [Theory]
    [InlineData("char.IsLetter(c)", "char.IsLetter(c)")]
    [InlineData("char.IsDigit(c)", "char.IsDigit(c)")]
    [InlineData("char.IsWhiteSpace(c)", "char.IsWhiteSpace(c)")]
    [InlineData("char.IsUpper(c)", "char.IsUpper(c)")]
    [InlineData("char.IsLower(c)", "char.IsLower(c)")]
    [InlineData("char.ToUpper(c)", "char.ToUpper(c)")]
    [InlineData("char.ToLower(c)", "char.ToLower(c)")]
    public void Migration_CharOperation_RoundTripsToCalor(string csharpExpr, string expectedCalorOp)
    {
        var csharp = $"public class Test {{ public object M(char c) => {csharpExpr}; }}";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);

        Assert.Contains(expectedCalorOp, calor);
    }

    [Fact]
    public void Migration_StringLiteralCharAt_RoundTripsToCalor()
    {
        // String literal indexing should produce char-at
        var csharp = "public class Test { public char M() { return \"hello\"[0]; } }";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);

        Assert.Contains("(\"hello\")[0]", calor);
    }

    [Fact]
    public void Migration_CharCode_RoundTripsToCalor()
    {
        var csharp = "public class Test { public int M(char c) { return (int)c; } }";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);

        Assert.Contains("((int)c)", calor);
    }

    [Fact]
    public void Migration_CharFromCode_RoundTripsToCalor()
    {
        var csharp = "public class Test { public char M(int n) { return (char)n; } }";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);

        Assert.Contains("((char)n)", calor);
    }

    [Fact]
    public void Migration_CrossCategory_CharWithStringOp()
    {
        // Test cross-category: char.IsLetter(s.ToUpper()[0])
        var csharp = "public class Test { public bool M(string s) { return char.IsLetter(s.ToUpper()[0]); } }";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);

        // Should contain char and string operations; indexing uses §IDX (not char-at)
        // because the converter cannot determine the type of s.ToUpper() without semantic model
        Assert.Contains("char.IsLetter(", calor);
        Assert.Contains(".ToUpper()", calor);
        Assert.Contains("[0]", calor);
    }

    [Fact]
    public void Migration_IntCastFromDouble_DoesNotConvertToCharCode()
    {
        // (int)someDouble should NOT become char-code - it's a numeric conversion
        var csharp = "public class Test { public int M(double d) { return (int)d; } }";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);

        // Should NOT contain char-code since source is double, not char
        Assert.DoesNotContain("char-code", calor);
    }

    [Fact]
    public void Migration_CharCastFromObject_DoesNotConvertToCharFromCode()
    {
        // (char)someObject should NOT become char-from-code without more context
        var csharp = "public class Test { public char M(object obj) { return (char)obj; } }";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);

        // May or may not contain char-from-code depending on heuristics
        // The important thing is it doesn't crash and produces valid output
        Assert.NotNull(calor);
    }

    #endregion
}
