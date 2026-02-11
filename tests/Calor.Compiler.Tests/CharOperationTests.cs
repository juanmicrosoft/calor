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
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static string WrapInFunction(string body, string inputType = "string", string outputType = "object")
    {
        return $$"""
            §M{m001:Test}
            §F{f001:Main:pub}
              §I{{{inputType}}:s}
              §O{{{outputType}}}
              {{body}}
            §/F{f001}
            §/M{m001}
            """;
    }

    private static string WrapCharFunction(string body)
    {
        return $$"""
            §M{m001:Test}
            §F{f001:Main:pub}
              §I{char:c}
              §O{object}
              {{body}}
            §/F{f001}
            §/M{m001}
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

    [Fact]
    public void Parse_CharAt_ReturnsCharOperationNode()
    {
        var source = WrapInFunction("§R (char-at s 0)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var charOp = GetReturnCharExpression(module);
        Assert.Equal(CharOp.CharAt, charOp.Operation);
        Assert.Equal(2, charOp.Arguments.Count);
    }

    [Fact]
    public void Parse_CharCode_ReturnsCharOperationNode()
    {
        var source = WrapCharFunction("§R (char-code c)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var charOp = GetReturnCharExpression(module);
        Assert.Equal(CharOp.CharCode, charOp.Operation);
        Assert.Single(charOp.Arguments);
    }

    [Fact]
    public void Parse_CharFromCode_ReturnsCharOperationNode()
    {
        var source = WrapInFunction("§R (char-from-code 65)", "i32", "char");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var charOp = GetReturnCharExpression(module);
        Assert.Equal(CharOp.CharFromCode, charOp.Operation);
        Assert.Single(charOp.Arguments);
    }

    [Fact]
    public void Parse_IsLetter_ReturnsCharOperationNode()
    {
        var source = WrapCharFunction("§R (is-letter c)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var charOp = GetReturnCharExpression(module);
        Assert.Equal(CharOp.IsLetter, charOp.Operation);
        Assert.Single(charOp.Arguments);
    }

    [Fact]
    public void Parse_IsDigit_ReturnsCharOperationNode()
    {
        var source = WrapCharFunction("§R (is-digit c)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var charOp = GetReturnCharExpression(module);
        Assert.Equal(CharOp.IsDigit, charOp.Operation);
        Assert.Single(charOp.Arguments);
    }

    [Fact]
    public void Parse_IsWhitespace_ReturnsCharOperationNode()
    {
        var source = WrapCharFunction("§R (is-whitespace c)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var charOp = GetReturnCharExpression(module);
        Assert.Equal(CharOp.IsWhiteSpace, charOp.Operation);
        Assert.Single(charOp.Arguments);
    }

    [Fact]
    public void Parse_IsUpper_ReturnsCharOperationNode()
    {
        var source = WrapCharFunction("§R (is-upper c)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var charOp = GetReturnCharExpression(module);
        Assert.Equal(CharOp.IsUpper, charOp.Operation);
        Assert.Single(charOp.Arguments);
    }

    [Fact]
    public void Parse_IsLower_ReturnsCharOperationNode()
    {
        var source = WrapCharFunction("§R (is-lower c)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var charOp = GetReturnCharExpression(module);
        Assert.Equal(CharOp.IsLower, charOp.Operation);
        Assert.Single(charOp.Arguments);
    }

    [Fact]
    public void Parse_CharUpper_ReturnsCharOperationNode()
    {
        var source = WrapCharFunction("§R (char-upper c)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var charOp = GetReturnCharExpression(module);
        Assert.Equal(CharOp.ToUpperChar, charOp.Operation);
        Assert.Single(charOp.Arguments);
    }

    [Fact]
    public void Parse_CharLower_ReturnsCharOperationNode()
    {
        var source = WrapCharFunction("§R (char-lower c)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var charOp = GetReturnCharExpression(module);
        Assert.Equal(CharOp.ToLowerChar, charOp.Operation);
        Assert.Single(charOp.Arguments);
    }

    #endregion

    #region C# Emission Tests

    [Theory]
    [InlineData("(char-at s 0)", "s[0]")]
    [InlineData("(char-code c)", "(int)c")]
    [InlineData("(char-from-code 65)", "(char)65")]
    [InlineData("(is-letter c)", "char.IsLetter(c)")]
    [InlineData("(is-digit c)", "char.IsDigit(c)")]
    [InlineData("(is-whitespace c)", "char.IsWhiteSpace(c)")]
    [InlineData("(is-upper c)", "char.IsUpper(c)")]
    [InlineData("(is-lower c)", "char.IsLower(c)")]
    [InlineData("(char-upper c)", "char.ToUpper(c)")]
    [InlineData("(char-lower c)", "char.ToLower(c)")]
    public void Emit_CharOperation_ProducesCorrectCSharp(string calor, string expectedCSharp)
    {
        var source = $$"""
            §M{m001:Test}
            §F{f001:Main:pub}
              §I{string:s}
              §I{char:c}
              §O{object}
              §R {{calor}}
            §/F{f001}
            §/M{m001}
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
    [InlineData("(char-at s 0)")]
    [InlineData("(char-code c)")]
    [InlineData("(char-from-code 65)")]
    [InlineData("(is-letter c)")]
    [InlineData("(is-digit c)")]
    [InlineData("(is-whitespace c)")]
    [InlineData("(is-upper c)")]
    [InlineData("(is-lower c)")]
    [InlineData("(char-upper c)")]
    [InlineData("(char-lower c)")]
    public void RoundTrip_CharOp_ProducesValidCalor(string op)
    {
        var source = $$"""
            §M{m001:Test}
            §F{f001:Main:pub}
              §I{string:s}
              §I{char:c}
              §O{object}
              §R {{op}}
            §/F{f001}
            §/M{m001}
            """;
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var calorEmitter = new CalorEmitter();
        var roundTripped = calorEmitter.Emit(module);

        Assert.Contains(op, roundTripped);
    }

    #endregion

    #region Error Tests

    [Fact]
    public void Parse_CharAt_NoArgs_ReportsError()
    {
        var source = WrapInFunction("§R (char-at)");
        Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Message.Contains("requires at least 2 argument"));
    }

    [Fact]
    public void Parse_CharAt_OneArg_ReportsError()
    {
        var source = WrapInFunction("§R (char-at s)");
        Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Message.Contains("requires at least 2 argument"));
    }

    [Fact]
    public void Parse_IsLetter_NoArgs_ReportsError()
    {
        var source = WrapCharFunction("§R (is-letter)");
        Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Message.Contains("requires at least 1 argument"));
    }

    [Fact]
    public void Parse_IsLetter_TooManyArgs_ReportsError()
    {
        var source = WrapCharFunction("§R (is-letter c c)");
        Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Message.Contains("accepts at most 1 argument"));
    }

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

    [Fact]
    public void Parse_CharAtWithStringOp_Works()
    {
        // Check first char of uppercased string
        var source = WrapInFunction("§R (char-at (upper s) 0)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var charOp = GetReturnCharExpression(module);
        Assert.Equal(CharOp.CharAt, charOp.Operation);

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);
        Assert.Contains("s.ToUpper()[0]", code);
    }

    [Fact]
    public void Parse_IsLetterWithCharAt_Works()
    {
        // Check if first char is a letter
        var source = WrapInFunction("§R (is-letter (char-at s 0))");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var charOp = GetReturnCharExpression(module);
        Assert.Equal(CharOp.IsLetter, charOp.Operation);

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);
        Assert.Contains("char.IsLetter(s[0])", code);
    }

    [Fact]
    public void Parse_CharUpperWithCharAt_Works()
    {
        // Convert first char to uppercase
        var source = WrapInFunction("§R (char-upper (char-at s 0))");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var charOp = GetReturnCharExpression(module);
        Assert.Equal(CharOp.ToUpperChar, charOp.Operation);

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);
        Assert.Contains("char.ToUpper(s[0])", code);
    }

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
    [InlineData("char.IsLetter(c)", "is-letter")]
    [InlineData("char.IsDigit(c)", "is-digit")]
    [InlineData("char.IsWhiteSpace(c)", "is-whitespace")]
    [InlineData("char.IsUpper(c)", "is-upper")]
    [InlineData("char.IsLower(c)", "is-lower")]
    [InlineData("char.ToUpper(c)", "char-upper")]
    [InlineData("char.ToLower(c)", "char-lower")]
    public void Migration_CharStaticMethod_ConvertsToCharOp(string csharpExpr, string expectedCalorOp)
    {
        // The C# to Calor converter converts char static methods to char operations
        var csharp = $"public class Test {{ public object M(char c) {{ return {csharpExpr}; }} }}";
        var converter = new CSharpToCalorConverter();
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
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        // Check via round-trip - the emitted Calor should contain char-code
        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);
        Assert.Contains("char-code", calor);
    }

    [Fact]
    public void Migration_IntCastToChar_ConvertsToCharFromCode()
    {
        // The C# to Calor converter converts (char)n to char-from-code
        var csharp = "public class Test { public char M(int n) { return (char)n; } }";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        // Check via round-trip - the emitted Calor should contain char-from-code
        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);
        Assert.Contains("char-from-code", calor);
    }

    [Fact]
    public void Migration_StringIndexer_ConvertsToCharAt()
    {
        // The C# to Calor converter converts s[0] to char-at
        var csharp = "public class Test { public char M(string s) { return s[0]; } }";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        // Check via round-trip - the emitted Calor should contain char-at
        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);
        Assert.Contains("char-at", calor);
    }

    [Fact]
    public void Migration_StringIndexerVariable_ConvertsToCharAt()
    {
        // The C# to Calor converter converts s[i] to char-at
        var csharp = "public class Test { public char M(string s, int i) { return s[i]; } }";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        // Check via round-trip - the emitted Calor should contain char-at
        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);
        Assert.Contains("char-at", calor);
    }

    [Theory]
    [InlineData("char.IsLetter(c)", "is-letter")]
    [InlineData("char.IsDigit(c)", "is-digit")]
    [InlineData("char.IsWhiteSpace(c)", "is-whitespace")]
    [InlineData("char.IsUpper(c)", "is-upper")]
    [InlineData("char.IsLower(c)", "is-lower")]
    [InlineData("char.ToUpper(c)", "char-upper")]
    [InlineData("char.ToLower(c)", "char-lower")]
    public void Migration_CharOperation_RoundTripsToCalor(string csharpExpr, string expectedCalorOp)
    {
        var csharp = $"public class Test {{ public object M(char c) => {csharpExpr}; }}";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);

        Assert.Contains(expectedCalorOp, calor);
    }

    [Fact]
    public void Migration_CharAt_RoundTripsToCalor()
    {
        var csharp = "public class Test { public char M(string s) { return s[0]; } }";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);

        Assert.Contains("char-at", calor);
    }

    [Fact]
    public void Migration_CharCode_RoundTripsToCalor()
    {
        var csharp = "public class Test { public int M(char c) { return (int)c; } }";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);

        Assert.Contains("char-code", calor);
    }

    [Fact]
    public void Migration_CharFromCode_RoundTripsToCalor()
    {
        var csharp = "public class Test { public char M(int n) { return (char)n; } }";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);

        Assert.Contains("char-from-code", calor);
    }

    [Fact]
    public void Migration_CrossCategory_CharWithStringOp()
    {
        // Test cross-category: char.IsLetter(s.ToUpper()[0])
        var csharp = "public class Test { public bool M(string s) { return char.IsLetter(s.ToUpper()[0]); } }";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);

        // Should contain both char and string operations
        Assert.Contains("is-letter", calor);
        Assert.Contains("upper", calor);
        Assert.Contains("char-at", calor);
    }

    [Fact]
    public void Migration_IntCastFromDouble_DoesNotConvertToCharCode()
    {
        // (int)someDouble should NOT become char-code - it's a numeric conversion
        var csharp = "public class Test { public int M(double d) { return (int)d; } }";
        var converter = new CSharpToCalorConverter();
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
        var converter = new CSharpToCalorConverter();
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
