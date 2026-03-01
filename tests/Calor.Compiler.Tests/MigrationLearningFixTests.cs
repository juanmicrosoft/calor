using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for compiler bugs identified from real-world migration learnings
/// (Newtonsoft.Json and Humanizer conversion campaigns).
/// </summary>
public class MigrationLearningFixTests
{
    private readonly CSharpToCalorConverter _converter = new();
    private static TextSpan Span => new(0, 1, 1, 1);

    #region Helpers

    private static string Emit(ModuleNode module)
    {
        var emitter = new CSharpEmitter();
        return emitter.Emit(module);
    }

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Success) return string.Empty;
        return string.Join("\n", result.Issues.Select(i => $"{i.Severity}: {i.Message}"));
    }

    /// <summary>
    /// Emits a single BindStatementNode through CSharpEmitter to test SanitizeIdentifier.
    /// </summary>
    private static string EmitBindStatement(string variableName)
    {
        var bindNode = new BindStatementNode(
            Span, variableName, "int", false,
            new IntLiteralNode(Span, 42),
            new AttributeCollection());

        var emitter = new CSharpEmitter();
        return bindNode.Accept(emitter);
    }

    #endregion

    #region PR 1: Complete Reserved Keyword Sanitization

    [Theory]
    [InlineData("as")]
    [InlineData("is")]
    [InlineData("in")]
    [InlineData("event")]
    [InlineData("var")]
    [InlineData("default")]
    [InlineData("lock")]
    [InlineData("delegate")]
    [InlineData("checked")]
    [InlineData("yield")]
    [InlineData("out")]
    [InlineData("ref")]
    [InlineData("volatile")]
    [InlineData("abstract")]
    [InlineData("override")]
    [InlineData("sealed")]
    [InlineData("virtual")]
    [InlineData("dynamic")]
    [InlineData("async")]
    [InlineData("await")]
    [InlineData("typeof")]
    [InlineData("sizeof")]
    [InlineData("unchecked")]
    [InlineData("unsafe")]
    [InlineData("fixed")]
    [InlineData("foreach")]
    [InlineData("goto")]
    [InlineData("throw")]
    [InlineData("try")]
    [InlineData("catch")]
    [InlineData("finally")]
    [InlineData("explicit")]
    [InlineData("implicit")]
    [InlineData("extern")]
    [InlineData("operator")]
    [InlineData("params")]
    [InlineData("readonly")]
    [InlineData("stackalloc")]
    [InlineData("const")]
    public void SanitizeIdentifier_NewReservedKeywords_PrefixedWithAt(string keyword)
    {
        var result = EmitBindStatement(keyword);

        // Should produce "int @keyword = 42;"
        Assert.Contains($"@{keyword}", result);
        Assert.DoesNotContain($"int {keyword} =", result);
    }

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    [InlineData("interface")]
    [InlineData("enum")]
    [InlineData("namespace")]
    [InlineData("using")]
    [InlineData("public")]
    [InlineData("private")]
    [InlineData("static")]
    [InlineData("void")]
    [InlineData("int")]
    [InlineData("string")]
    [InlineData("bool")]
    [InlineData("return")]
    [InlineData("if")]
    [InlineData("else")]
    [InlineData("for")]
    [InlineData("while")]
    [InlineData("switch")]
    [InlineData("case")]
    [InlineData("break")]
    [InlineData("continue")]
    [InlineData("new")]
    public void SanitizeIdentifier_OriginalKeywords_StillPrefixed(string keyword)
    {
        var result = EmitBindStatement(keyword);
        Assert.Contains($"@{keyword}", result);
    }

    [Fact]
    public void SanitizeIdentifier_NonKeyword_NotPrefixed()
    {
        var result = EmitBindStatement("myVariable");
        Assert.Contains("myVariable", result);
        Assert.DoesNotContain("@myVariable", result);
    }

    #endregion

    #region PR 2: Call Expression Leading Dot

    [Fact]
    public void CallExpression_LeadingDot_PrependedWithThis()
    {
        var callNode = new CallExpressionNode(
            Span, ".DoSomething",
            Array.Empty<ExpressionNode>());

        var emitter = new CSharpEmitter();
        var result = callNode.Accept(emitter);

        Assert.Equal("this.DoSomething()", result);
    }

    [Fact]
    public void CallExpression_LeadingDot_WithArguments()
    {
        var callNode = new CallExpressionNode(
            Span, ".Process",
            new List<ExpressionNode> { new IntLiteralNode(Span, 42) });

        var emitter = new CSharpEmitter();
        var result = callNode.Accept(emitter);

        Assert.Equal("this.Process(42)", result);
    }

    [Fact]
    public void CallExpression_NoDot_NotPrepended()
    {
        var callNode = new CallExpressionNode(
            Span, "Console.WriteLine",
            new List<ExpressionNode> { new StringLiteralNode(Span, "hello") });

        var emitter = new CSharpEmitter();
        var result = callNode.Accept(emitter);

        Assert.Equal("Console.WriteLine(\"hello\")", result);
    }

    [Fact]
    public void CallExpression_DotInMiddle_NotPrepended()
    {
        var callNode = new CallExpressionNode(
            Span, "obj.Method",
            Array.Empty<ExpressionNode>());

        var emitter = new CSharpEmitter();
        var result = callNode.Accept(emitter);

        Assert.Equal("obj.Method()", result);
        Assert.DoesNotContain("this", result);
    }

    #endregion

    #region PR 3: Module ID Consistency

    [Fact]
    public void Convert_ModuleId_AlwaysM001()
    {
        // Even with many members, module ID should be m001
        var csharpSource = """
            namespace TestNamespace
            {
                public class ClassA
                {
                    public int FieldA;
                    public int FieldB;
                    public void Method1() { }
                    public void Method2() { }
                    public void Method3() { }
                    public void Method4() { }
                    public void Method5() { }
                    public int Prop1 { get; set; }
                    public int Prop2 { get; set; }
                    public int Prop3 { get; set; }
                }

                public class ClassB
                {
                    public void Method6() { }
                    public void Method7() { }
                    public void Method8() { }
                }

                public enum MyEnum
                {
                    Value1,
                    Value2,
                    Value3
                }
            }
            """;

        var result = _converter.Convert(csharpSource);
        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        Assert.Equal("m001", result.Ast.Id);
    }

    [Fact]
    public void Convert_SimpleClass_ModuleIdIsM001()
    {
        var csharpSource = """
            namespace Simple
            {
                public class Foo
                {
                    public int Bar() { return 42; }
                }
            }
            """;

        var result = _converter.Convert(csharpSource);
        Assert.True(result.Success, GetErrorMessage(result));
        Assert.Equal("m001", result.Ast!.Id);
    }

    #endregion

    #region PR 4: Interop Block Namespace Duplication

    [Fact]
    public void Interop_ConvertedClass_NoDoubleNamespace()
    {
        var csharpSource = """
            namespace MyLib
            {
                public class SimpleClass
                {
                    public void DoWork() { }
                }
            }
            """;

        var result = _converter.Convert(csharpSource);
        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        Assert.Equal("MyLib", result.Ast.Name);

        var emitted = Emit(result.Ast);
        var namespaceCount = CountOccurrences(emitted, "namespace MyLib");
        Assert.Equal(1, namespaceCount);
    }

    [Fact]
    public void Interop_MultipleClasses_NoDoubleNamespace()
    {
        var csharpSource = """
            namespace MyProject
            {
                public class ClassOne
                {
                    public int Value { get; set; }
                }

                public class ClassTwo
                {
                    public string Name { get; set; }
                }
            }
            """;

        var result = _converter.Convert(csharpSource);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitted = Emit(result.Ast!);
        var namespaceCount = CountOccurrences(emitted, "namespace MyProject");
        Assert.Equal(1, namespaceCount);
    }

    [Fact]
    public void RoundTrip_KeywordIdentifiers_SanitizedInCSharpOutput()
    {
        // Test the full round-trip: C# with @keyword identifiers → Calor → C#
        var csharpSource = """
            namespace TestKeywords
            {
                public class KeywordTest
                {
                    public int Run()
                    {
                        int @event = 10;
                        int @lock = 20;
                        int @is = 30;
                        return @event + @lock + @is;
                    }
                }
            }
            """;

        var result = _converter.Convert(csharpSource);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitted = Emit(result.Ast!);
        // Keywords should be sanitized with @ prefix in emitted C#
        Assert.Contains("@event", emitted);
        Assert.Contains("@lock", emitted);
        Assert.Contains("@is", emitted);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }

    #endregion
}
