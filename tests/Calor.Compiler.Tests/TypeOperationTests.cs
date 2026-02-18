using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class TypeOperationTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static string WrapInFunction(string body, string inputType = "object", string outputType = "object")
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

    private static TypeOperationNode GetReturnTypeExpression(ModuleNode module)
    {
        var func = module.Functions[0];
        var returnStmt = func.Body[0] as ReturnStatementNode;
        Assert.NotNull(returnStmt);
        var typeOp = returnStmt!.Expression as TypeOperationNode;
        Assert.NotNull(typeOp);
        return typeOp!;
    }

    #region AST Tests

    [Fact]
    public void Parse_Cast_ReturnsTypeOperationNode()
    {
        var source = WrapInFunction("§R (cast i32 x)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var typeOp = GetReturnTypeExpression(module);
        Assert.Equal(TypeOp.Cast, typeOp.Operation);
        Assert.Equal("i32", typeOp.TargetType);
        Assert.IsType<ReferenceNode>(typeOp.Operand);
        Assert.Equal("x", ((ReferenceNode)typeOp.Operand).Name);
    }

    [Fact]
    public void Parse_Is_ReturnsTypeOperationNode()
    {
        var source = WrapInFunction("§R (is x str)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var typeOp = GetReturnTypeExpression(module);
        Assert.Equal(TypeOp.Is, typeOp.Operation);
        Assert.Equal("str", typeOp.TargetType);
        Assert.IsType<ReferenceNode>(typeOp.Operand);
        Assert.Equal("x", ((ReferenceNode)typeOp.Operand).Name);
    }

    [Fact]
    public void Parse_As_ReturnsTypeOperationNode()
    {
        var source = WrapInFunction("§R (as x str)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var typeOp = GetReturnTypeExpression(module);
        Assert.Equal(TypeOp.As, typeOp.Operation);
        Assert.Equal("str", typeOp.TargetType);
        Assert.IsType<ReferenceNode>(typeOp.Operand);
        Assert.Equal("x", ((ReferenceNode)typeOp.Operand).Name);
    }

    [Fact]
    public void Parse_Cast_WithCustomType_ReturnsTypeOperationNode()
    {
        var source = WrapInFunction("§R (cast MyClass x)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var typeOp = GetReturnTypeExpression(module);
        Assert.Equal(TypeOp.Cast, typeOp.Operation);
        Assert.Equal("MyClass", typeOp.TargetType);
    }

    #endregion

    #region C# Emission Tests

    [Theory]
    [InlineData("(cast i32 x)", "(int)x")]
    [InlineData("(cast f64 x)", "(double)x")]
    [InlineData("(cast str x)", "(string)x")]
    [InlineData("(is x str)", "x is string")]
    [InlineData("(is x i32)", "x is int")]
    [InlineData("(as x str)", "x as string")]
    [InlineData("(as x MyClass)", "x as MyClass")]
    public void Emit_TypeOperation_ProducesCorrectCSharp(string calor, string expectedCSharp)
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
    [InlineData("(cast i32 x)")]
    [InlineData("(cast f64 x)")]
    [InlineData("(cast str x)")]
    [InlineData("(is x str)")]
    [InlineData("(is x i32)")]
    [InlineData("(is x MyClass)")]
    [InlineData("(as x str)")]
    [InlineData("(as x MyClass)")]
    public void RoundTrip_TypeOp_ProducesValidCalor(string op)
    {
        var source = WrapInFunction($"§R {op}");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var calorEmitter = new CalorEmitter();
        var roundTripped = calorEmitter.Emit(module);

        Assert.Contains(op, roundTripped);
    }

    #endregion

    #region Error Tests

    [Fact]
    public void Parse_Cast_NoArgs_ReportsError()
    {
        var source = WrapInFunction("§R (cast)");
        Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Message.Contains("requires exactly 2 arguments"));
    }

    [Fact]
    public void Parse_Cast_OneArg_ReportsError()
    {
        var source = WrapInFunction("§R (cast i32)");
        Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Message.Contains("requires exactly 2 arguments"));
    }

    [Fact]
    public void Parse_Cast_ThreeArgs_ReportsError()
    {
        var source = WrapInFunction("§R (cast i32 x y)");
        Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Message.Contains("requires exactly 2 arguments"));
    }

    [Fact]
    public void Parse_Is_NoArgs_ReportsError()
    {
        var source = WrapInFunction("§R (is)");
        Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Message.Contains("requires exactly 2 arguments"));
    }

    [Fact]
    public void Parse_As_OneArg_ReportsError()
    {
        var source = WrapInFunction("§R (as x)");
        Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Message.Contains("requires exactly 2 arguments"));
    }

    [Fact]
    public void Parse_Cast_NonIdentifierType_ReportsError()
    {
        var source = WrapInFunction("§R (cast 42 x)");
        Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Message.Contains("Expected a type name"));
    }

    [Fact]
    public void Parse_Is_NonIdentifierType_ReportsError()
    {
        var source = WrapInFunction("§R (is x 42)");
        Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Message.Contains("Expected a type name"));
    }

    #endregion

    #region Extension Tests

    [Theory]
    [InlineData("cast", TypeOp.Cast)]
    [InlineData("is", TypeOp.Is)]
    [InlineData("as", TypeOp.As)]
    public void FromString_ValidOperator_ReturnsCorrectEnum(string name, TypeOp expected)
    {
        var result = TypeOpExtensions.FromString(name);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FromString_InvalidOperator_ReturnsNull()
    {
        var result = TypeOpExtensions.FromString("invalid");
        Assert.Null(result);
    }

    [Fact]
    public void FromString_Null_ReturnsNull()
    {
        var result = TypeOpExtensions.FromString(null);
        Assert.Null(result);
    }

    [Theory]
    [InlineData(TypeOp.Cast, "cast")]
    [InlineData(TypeOp.Is, "is")]
    [InlineData(TypeOp.As, "as")]
    public void ToCalorName_ReturnsCorrectName(TypeOp op, string expected)
    {
        var result = op.ToCalorName();
        Assert.Equal(expected, result);
    }

    #endregion

    #region Migration Tests

    private static TypeOperationNode? FindTypeOperationInResult(ConversionResult result)
    {
        if (!result.Success || result.Ast == null) return null;

        foreach (var func in result.Ast.Functions)
        {
            var found = FindTypeOperationInBody(func.Body);
            if (found != null) return found;
        }

        foreach (var cls in result.Ast.Classes)
        {
            foreach (var method in cls.Methods)
            {
                var found = FindTypeOperationInBody(method.Body);
                if (found != null) return found;
            }
        }

        return null;
    }

    private static TypeOperationNode? FindTypeOperationInBody(IReadOnlyList<StatementNode> body)
    {
        foreach (var stmt in body)
        {
            var found = FindTypeOperationInStatement(stmt);
            if (found != null) return found;
        }
        return null;
    }

    private static TypeOperationNode? FindTypeOperationInStatement(StatementNode stmt)
    {
        if (stmt is ReturnStatementNode ret)
            return FindTypeOperationInExpression(ret.Expression);
        if (stmt is BindStatementNode bind)
            return FindTypeOperationInExpression(bind.Initializer);
        return null;
    }

    private static TypeOperationNode? FindTypeOperationInExpression(ExpressionNode? expr)
    {
        if (expr == null) return null;
        if (expr is TypeOperationNode typeOp) return typeOp;
        return null;
    }

    [Fact]
    public void Migration_CSharpCast_ConvertsToTypeOperationNode()
    {
        var csharp = "public class Test { public int M(object x) { return (int)x; } }";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var typeOp = FindTypeOperationInResult(result);
        Assert.NotNull(typeOp);
        Assert.Equal(TypeOp.Cast, typeOp!.Operation);
        Assert.Equal("i32", typeOp.TargetType);
    }

    [Fact]
    public void Migration_CSharpIs_ConvertsToTypeOperationNode()
    {
        var csharp = "public class Test { public bool M(object x) { return x is string; } }";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var typeOp = FindTypeOperationInResult(result);
        Assert.NotNull(typeOp);
        Assert.Equal(TypeOp.Is, typeOp!.Operation);
        Assert.Equal("str", typeOp.TargetType);
    }

    [Fact]
    public void Migration_CSharpAs_ConvertsToTypeOperationNode()
    {
        var csharp = "public class Test { public string M(object x) { return x as string; } }";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var typeOp = FindTypeOperationInResult(result);
        Assert.NotNull(typeOp);
        Assert.Equal(TypeOp.As, typeOp!.Operation);
        Assert.Equal("str", typeOp.TargetType);
    }

    [Fact]
    public void Migration_CSharpCast_RoundTripsToCalor()
    {
        var csharp = "public class Test { public int M(object x) { return (int)x; } }";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);

        Assert.Contains("(cast i32 x)", calor);
    }

    [Fact]
    public void Migration_CSharpAs_RoundTripsToCalor()
    {
        var csharp = "public class Test { public string M(object x) { return x as string; } }";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);

        Assert.Contains("(as x str)", calor);
    }

    [Fact]
    public void Migration_CSharpIs_RoundTripsToCalor()
    {
        var csharp = "public class Test { public bool M(object x) { return x is string; } }";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);

        Assert.Contains("(is x str)", calor);
    }

    [Fact]
    public void Migration_CSharpIsDeclaration_ConvertsToTypeOperationNode()
    {
        // "x is string s" — declaration pattern, variable binding is not preserved but type check is
        var csharp = "public class Test { public bool M(object x) { if (x is string s) return true; return false; } }";
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);

        Assert.Contains("(is x str)", calor);
    }

    #endregion

    #region Composition Tests

    [Fact]
    public void Parse_CastWithBinaryOp_Works()
    {
        var source = WrapInFunction("§R (cast i32 (+ x 1))");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var typeOp = GetReturnTypeExpression(module);
        Assert.Equal(TypeOp.Cast, typeOp.Operation);
        Assert.IsType<BinaryOperationNode>(typeOp.Operand);

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);
        Assert.Contains("(int)(x + 1)", code);
    }

    [Fact]
    public void Parse_AsInReturnExpression_Works()
    {
        var source = WrapInFunction("§R (as x str)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);
        Assert.Contains("x as string", code);
    }

    [Fact]
    public void Parse_NestedCast_Works()
    {
        // (cast i32 (cast f64 x)) — cast to f64 then to i32
        var source = WrapInFunction("§R (cast i32 (cast f64 x))");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var outer = GetReturnTypeExpression(module);
        Assert.Equal(TypeOp.Cast, outer.Operation);
        Assert.Equal("i32", outer.TargetType);

        var inner = Assert.IsType<TypeOperationNode>(outer.Operand);
        Assert.Equal(TypeOp.Cast, inner.Operation);
        Assert.Equal("f64", inner.TargetType);

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);
        Assert.Contains("(int)(double)x", code);
    }

    [Fact]
    public void Parse_IsNestedInCast_Works()
    {
        // Unusual but valid: (cast i32 (is x str)) — cast bool result to int
        var source = WrapInFunction("§R (cast i32 (is x str))");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var outer = GetReturnTypeExpression(module);
        Assert.Equal(TypeOp.Cast, outer.Operation);
        Assert.Equal("i32", outer.TargetType);

        var inner = Assert.IsType<TypeOperationNode>(outer.Operand);
        Assert.Equal(TypeOp.Is, inner.Operation);

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);
        Assert.Contains("(int)x is string", code);
    }

    [Fact]
    public void RoundTrip_NestedCast_PreservesStructure()
    {
        var source = WrapInFunction("§R (cast i32 (cast f64 x))");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var calorEmitter = new CalorEmitter();
        var roundTripped = calorEmitter.Emit(module);

        Assert.Contains("(cast i32 (cast f64 x))", roundTripped);
    }

    #endregion

    #region Type Checker Tests

    [Fact]
    public void TypeChecker_AsWithValueType_ReportsWarning()
    {
        var source = WrapInFunction("§R (as x i32)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var typeChecker = new Calor.Compiler.TypeChecking.TypeChecker(diagnostics);
        typeChecker.Check(module);

        Assert.Contains(diagnostics, d =>
            d.Message.Contains("'as' operator cannot be used with value type") &&
            d.Message.Contains("i32"));
    }

    [Fact]
    public void TypeChecker_AsWithReferenceType_NoWarning()
    {
        var source = WrapInFunction("§R (as x str)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var typeChecker = new Calor.Compiler.TypeChecking.TypeChecker(diagnostics);
        typeChecker.Check(module);

        Assert.DoesNotContain(diagnostics, d =>
            d.Message.Contains("'as' operator cannot be used with value type"));
    }

    [Fact]
    public void TypeChecker_AsWithBool_ReportsWarning()
    {
        var source = WrapInFunction("§R (as x bool)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var typeChecker = new Calor.Compiler.TypeChecking.TypeChecker(diagnostics);
        typeChecker.Check(module);

        Assert.Contains(diagnostics, d =>
            d.Message.Contains("'as' operator cannot be used with value type"));
    }

    [Fact]
    public void TypeChecker_AsWithFloat_ReportsWarning()
    {
        var source = WrapInFunction("§R (as x f64)");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var typeChecker = new Calor.Compiler.TypeChecking.TypeChecker(diagnostics);
        typeChecker.Check(module);

        Assert.Contains(diagnostics, d =>
            d.Message.Contains("'as' operator cannot be used with value type"));
    }

    #endregion

    #region OperatorSuggestions Tests

    [Fact]
    public void GetTypeOpExample_Cast_ReturnsExample()
    {
        var example = OperatorSuggestions.GetTypeOpExample("cast");
        Assert.Equal("(cast i32 expr)", example);
    }

    [Fact]
    public void GetTypeOpExample_Is_ReturnsExample()
    {
        var example = OperatorSuggestions.GetTypeOpExample("is");
        Assert.Equal("(is expr MyType)", example);
    }

    [Fact]
    public void GetTypeOpExample_As_ReturnsExample()
    {
        var example = OperatorSuggestions.GetTypeOpExample("as");
        Assert.Equal("(as expr str)", example);
    }

    [Fact]
    public void GetOperatorCategories_IncludesTypeOperations()
    {
        var categories = OperatorSuggestions.GetOperatorCategories();
        Assert.Contains("type (cast, is, as)", categories);
    }

    #endregion
}
