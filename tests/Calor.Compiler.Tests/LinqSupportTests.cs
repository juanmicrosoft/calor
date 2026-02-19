using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for LINQ support features: decimal literals, array initializers, object initializers,
/// anonymous types, LINQ method chains, and LINQ query syntax.
/// </summary>
public class LinqSupportTests
{
    #region Helpers

    private static string ParseAndEmit(string source)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        return emitter.Emit(module);
    }

    private readonly CSharpToCalorConverter _converter = new();

    private string GetErrorMessage(ConversionResult result)
    {
        return string.Join("\n", result.Issues.Select(d => d.ToString()));
    }

    #endregion

    #region Feature 1: Decimal Literals

    [Fact]
    public void DecimalLiteral_TypedSyntax_EmitsCorrectly()
    {
        var source = @"
§M{m1:TestMod}
§F{f1:Test:pub}
  §B{~price:decimal} DECIMAL:18.0000
§/F{f1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("18.0000m", result);
    }

    [Fact]
    public void DecimalLiteral_InlineSuffix_EmitsCorrectly()
    {
        var source = @"
§M{m1:TestMod}
§F{f1:Test:pub}
  §B{~amount:decimal} 99.95M
§/F{f1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("99.95m", result);
    }

    [Fact]
    public void DecimalLiteral_IntegerWithSuffix_EmitsCorrectly()
    {
        var source = @"
§M{m1:TestMod}
§F{f1:Test:pub}
  §B{~count:decimal} 42m
§/F{f1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("42m", result);
    }

    [Fact]
    public void DecimalLiteral_Converter_FromCSharp()
    {
        var csharpSource = """
            decimal price = 18.0000m;
            decimal total = 100m;
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        // The converted Calor source should contain decimal values
        Assert.Contains("18.0000", result.CalorSource);
    }

    #endregion

    #region Feature 2: Array Initializers

    [Fact]
    public void ArrayInitializer_WithArgElements_EmitsCorrectly()
    {
        var source = @"
§M{m1:TestMod}
§F{f1:Test:pub}
  §B{[i32]:nums} §ARR{a1:i32} §A 1 §A 2 §A 3 §/ARR{a1}
§/F{f1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("new int[]", result);
        Assert.Contains("1, 2, 3", result);
    }

    [Fact]
    public void ArrayInitializer_TypeFirst_EmitsCorrectly()
    {
        // Type-first format: §ARR{type:id:size}
        var source = @"
§M{m1:TestMod}
§F{f1:Test:pub}
  §B{[i32]:nums} §ARR{i32:nums:3}
§/F{f1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("new int[3]", result);
    }

    [Fact]
    public void ArrayInitializer_Converter_ImplicitArray()
    {
        var csharpSource = """
            var nums = new[] { 1, 2, 3 };
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
    }

    [Fact]
    public void ArrayInitializer_Converter_BareDoubleArray()
    {
        var csharpSource = """
            double[] arr = { 1.7, 2.3, 1.9 };
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("§ARR{", result.CalorSource);
        Assert.DoesNotContain("§ERR", result.CalorSource);
    }

    [Fact]
    public void ArrayInitializer_Converter_BareIntArray()
    {
        var csharpSource = """
            int[] nums = { 1, 2, 3, 4, 5 };
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.DoesNotContain("§ERR", result.CalorSource);
    }

    [Fact]
    public void ArrayInitializer_Converter_BareStringArray()
    {
        var csharpSource = """
            string[] names = { "Alice", "Bob" };
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.DoesNotContain("§ERR", result.CalorSource);
    }

    [Fact]
    public void ArrayInitializer_Converter_UseDeclaredTypeOverLiteralInference()
    {
        // int literals in a double[] should infer f64 from declaration, not i32 from literals
        var csharpSource = """
            double[] arr = { 1, 2, 3 };
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.DoesNotContain("§ERR", result.CalorSource);
        Assert.Contains("§ARR{arr:f64}", result.CalorSource);
    }

    [Fact]
    public void ArrayInitializer_Converter_EmptyArray()
    {
        var csharpSource = """
            int[] arr = { };
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.DoesNotContain("§ERR", result.CalorSource);
        Assert.Contains("§ARR{i32:", result.CalorSource);
    }

    [Fact]
    public void ArrayInitializer_Converter_MultiDimensionalUsesCorrectElementType()
    {
        var csharpSource = """
            int[,] matrix = { { 1, 2 }, { 3, 4 } };
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        // Element type should be inferred from the int[,] declaration
        Assert.Contains("§ARR{matrix:i32}", result.CalorSource);
    }

    #endregion

    #region Feature 3: Object Initializers

    [Fact]
    public void ObjectInitializer_BasicProperties_EmitsCorrectly()
    {
        var source = @"
§M{m1:TestMod}
§F{f1:Test:pub}
  §B{~p:Person} §NEW{Person}
    Name = ""John""
    Age = 30
  §/NEW
§/F{f1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("new Person()", result);
        Assert.Contains("Name = \"John\"", result);
        Assert.Contains("Age = 30", result);
    }

    [Fact]
    public void ObjectInitializer_EmitsInitializerBlock()
    {
        var source = @"
§M{m1:TestMod}
§F{f1:Test:pub}
  §B{~cfg:Config} §NEW{Config}
    Timeout = 30
    Enabled = true
  §/NEW
§/F{f1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("new Config()", result);
        Assert.Contains("Timeout = 30", result);
        Assert.Contains("Enabled = true", result);
    }

    [Fact]
    public void ObjectInitializer_Converter_PreservesProperties()
    {
        var csharpSource = """
            var p = new Person { Name = "Alice", Age = 25 };
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("§NEW{Person}", result.CalorSource);
        Assert.Contains("Name =", result.CalorSource);
        Assert.Contains("Age =", result.CalorSource);
    }

    #endregion

    #region Feature 4: Anonymous Types

    [Fact]
    public void AnonymousType_BasicSyntax_EmitsNewAnonymous()
    {
        var source = @"
§M{m1:TestMod}
§F{f1:Test:pub}
  §B{~obj:var} §ANON
    Name = ""test""
    Value = 42
  §/ANON
§/F{f1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("new {", result);
        Assert.Contains("Name = \"test\"", result);
        Assert.Contains("Value = 42", result);
    }

    [Fact]
    public void AnonymousType_Converter_FromCSharp()
    {
        var csharpSource = """
            var obj = new { Name = "test", Value = 42 };
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("§ANON", result.CalorSource);
        Assert.Contains("Name =", result.CalorSource);
        Assert.Contains("Value =", result.CalorSource);
        Assert.Contains("anonymous-type", result.Context.UsedFeatures);
    }

    #endregion

    #region Feature 5: LINQ Method Chains

    [Fact]
    public void LinqMethodChain_Converter_SimpleWhere()
    {
        var csharpSource = """
            var evens = numbers.Where(n => n % 2 == 0);
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("Where", result.CalorSource);
    }

    [Fact]
    public void LinqMethodChain_Converter_ChainedCalls()
    {
        var csharpSource = """
            var result = numbers.Where(n => n > 0).Select(n => n * 2);
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("Where", result.CalorSource);
        Assert.Contains("Select", result.CalorSource);
        Assert.Contains("linq-method", result.Context.UsedFeatures);
    }

    [Fact]
    public void LinqMethodChain_Converter_GroupBySelect()
    {
        var csharpSource = """
            var groups = products.GroupBy(p => p.Category).Select(g => g.Key);
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("GroupBy", result.CalorSource);
        Assert.Contains("Select", result.CalorSource);
    }

    #endregion

    #region Feature 6: LINQ Query Syntax

    [Fact]
    public void LinqQuery_Converter_SimpleWhereSelect()
    {
        var csharpSource = """
            var evens = from n in numbers
                        where n % 2 == 0
                        select n;
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("Where", result.CalorSource);
        Assert.Contains("linq-query", result.Context.UsedFeatures);
    }

    [Fact]
    public void LinqQuery_Converter_GroupBy()
    {
        var csharpSource = """
            var grouped = from p in products
                          group p by p.Category;
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("GroupBy", result.CalorSource);
    }

    [Fact]
    public void LinqQuery_Converter_OrderBy()
    {
        var csharpSource = """
            var sorted = from p in products
                         orderby p.Name
                         select p;
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("OrderBy", result.CalorSource);
    }

    [Fact]
    public void LinqQuery_Converter_OrderByDescending()
    {
        var csharpSource = """
            var sorted = from p in products
                         orderby p.Price descending
                         select p;
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("OrderByDescending", result.CalorSource);
    }

    [Fact]
    public void LinqQuery_Converter_GroupByIntoSelect()
    {
        var csharpSource = """
            var result = from p in products
                         group p by p.Category into g
                         select g.Key;
            """;

        var result = _converter.Convert(csharpSource);

        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.Contains("GroupBy", result.CalorSource);
        Assert.Contains("Select", result.CalorSource);
    }

    #endregion

    #region Feature Support Level

    [Fact]
    public void FeatureSupport_LinqMethod_IsFullySupported()
    {
        Assert.True(FeatureSupport.IsFullySupported("linq-method"));
    }

    [Fact]
    public void FeatureSupport_LinqQuery_IsFullySupported()
    {
        Assert.True(FeatureSupport.IsFullySupported("linq-query"));
    }

    [Fact]
    public void FeatureSupport_ArrayInitializer_IsFullySupported()
    {
        Assert.True(FeatureSupport.IsFullySupported("array-initializer"));
    }

    #endregion
}
