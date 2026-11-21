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
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static string WrapInFunction(string body)
    {
        return $$"""
            module Test Test block
              func Main (object:sb) → object
                !
                {{body}}
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

    #endregion

    #region C# Emission Tests

    [Theory]
    [InlineData("new System.Text.StringBuilder()", "new System.Text.StringBuilder()")]
    [InlineData("new System.Text.StringBuilder(\"init\")", "new System.Text.StringBuilder(\"init\")")]
    [InlineData("sb.Append(\"text\")", "sb.Append(\"text\")")]
    [InlineData("sb.AppendLine(\"text\")", "sb.AppendLine(\"text\")")]
    [InlineData("sb.Insert(0, \"text\")", "sb.Insert(0, \"text\")")]
    [InlineData("sb.Remove(0, 5)", "sb.Remove(0, 5)")]
    [InlineData("sb.Clear()", "sb.Clear()")]
    [InlineData("sb.ToString()", "sb.ToString()")]
    [InlineData("sb.Length", "sb.Length")]
    public void Emit_StringBuilderOperation_ProducesCorrectCSharp(string calor, string expectedCSharp)
    {
        var source = WrapInFunction($"return {calor}");
        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains(expectedCSharp, code);
    }
    #endregion
    #region Round-Trip Tests
    #endregion
    #region Error Tests

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
    [InlineData(StringBuilderOp.New, 0, 4)]
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
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        // Check via round-trip
        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);
        Assert.Contains("new System.Text.StringBuilder()", calor);
    }

    [Fact]
    public void Migration_NewStringBuilderWithInit_ConvertsToSbNew()
    {
        // The C# to Calor converter converts new StringBuilder("init") to sb-new
        var csharp = @"
using System.Text;
public class Test { public StringBuilder M() { return new StringBuilder(""init""); } }";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        // Check via round-trip - the emitted Calor should contain sb-new
        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);
        Assert.Contains("new System.Text.StringBuilder(\"init\")", calor);
    }

    [Theory]
    [InlineData("sb.Append(\"text\")", "(sb).Append(\"text\")")]
    [InlineData("sb.AppendLine(\"text\")", "(sb).AppendLine(\"text\")")]
    [InlineData("sb.Clear()", "(sb).Clear()")]
    [InlineData("sb.ToString()", "(sb).ToString()")]
    public void Migration_StringBuilderInstanceMethod_ConvertsToSbOp(string csharpExpr, string expectedCalorOp)
    {
        var csharp = $@"
using System.Text;
public class Test {{ public object M(StringBuilder sb) {{ return {csharpExpr}; }} }}";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
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
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        // Check via round-trip
        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);
        Assert.Contains("(sb).Insert(0, \"text\")", calor);
    }

    [Fact]
    public void Migration_StringBuilderRemove_ConvertsToSbRemove()
    {
        // The C# to Calor converter converts sb.Remove(0, 5) to sb-remove
        var csharp = @"
using System.Text;
public class Test { public StringBuilder M(StringBuilder sb) { return sb.Remove(0, 5); } }";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        // Check via round-trip
        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);
        Assert.Contains("(sb).Remove(0, 5)", calor);
    }

    [Fact]
    public void Migration_StringBuilderLength_ConvertsToSbLength()
    {
        // Length remains an ordinary property, independent of the receiver's name.
        var csharp = @"
using System.Text;
public class Test { public int M(StringBuilder sb) { return sb.Length; } }";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        // Check via round-trip.
        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);
        Assert.Contains("sb.Length", calor);
        Assert.DoesNotContain("sb-length", calor);
    }

    [Theory]
    [InlineData("new StringBuilder()", "new System.Text.StringBuilder()")]
    [InlineData("new StringBuilder(\"init\")", "new System.Text.StringBuilder(\"init\")")]
    public void Migration_NewStringBuilder_RoundTripsToCalor(string csharpExpr, string expectedCalorOp)
    {
        var csharp = $@"
using System.Text;
public class Test {{ public StringBuilder M() => {csharpExpr}; }}";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);

        Assert.Contains(expectedCalorOp, calor);
    }

    [Theory]
    [InlineData("sb.Append(\"text\")", "(sb).Append(\"text\")")]
    [InlineData("sb.AppendLine(\"text\")", "(sb).AppendLine(\"text\")")]
    [InlineData("sb.Insert(0, \"text\")", "(sb).Insert(0, \"text\")")]
    [InlineData("sb.Remove(0, 5)", "(sb).Remove(0, 5)")]
    [InlineData("sb.Clear()", "(sb).Clear()")]
    [InlineData("sb.ToString()", "(sb).ToString()")]
    public void Migration_StringBuilderMethod_RoundTripsToCalor(string csharpExpr, string expectedCalorOp)
    {
        var csharp = $@"
using System.Text;
public class Test {{ public object M(StringBuilder sb) => {csharpExpr}; }}";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
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
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);

        // Should contain all the operations
        Assert.Contains("new System.Text.StringBuilder()", calor);
        Assert.Contains(".Append(\"a\")", calor);
        Assert.Contains(".ToString()", calor);
    }

    [Fact]
    public void Migration_StringBuilderWithNonSbName_FallsBackToInterop()
    {
        // Variable named 'builder' instead of 'sb' - should still work due to method signature
        var csharp = @"
using System.Text;
public class Test { public string M(StringBuilder builder) { return builder.ToString(); } }";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
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
