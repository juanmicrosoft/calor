using System.Reflection;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Migration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public class LinqGroupingSemanticsTests
{
    [Theory]
    [InlineData("from n in values group n * 10 by n % 2", "1:10|0:20", "", false)]
    [InlineData("from n in values group n by n % 2", "1:1|0:2", "", false)]
    [InlineData("from n in values group Element(n) by Key(n)", "1:10|0:20", "K1E1K2E2", false)]
    [InlineData("from n in values group Element(n).ToString().Length by Key(n)", "1:2|0:2", "K1E1K2E2", true)]
    [InlineData("from n in values where Pass(n) group Element(n) by Key(n)", "1:10|0:20", "W1K1E1W2K2E2", false)]
    [InlineData("from n in values orderby n descending group Element(n) by Key(n)", "0:20|1:10", "K2E2K1E1", false)]
    [InlineData("from n in values group Element(n) by Key(n) into g select g", "1:10|0:20", "K1E1K2E2", false)]
    [InlineData("from n in values group n * 10 by n % 2 into g orderby g.Key select g", "0:20|1:10", "", true)]
    [InlineData("from n in values let scaled = Element(n) group scaled by Key(n)", "1:10|0:20", "E1K1E2K2", true)]
    [InlineData("from n in values join m in values on n equals m group n + m by n % 2", "1:2|0:4", "", true)]
    [InlineData("from n in values from m in new[] {10} group n * m by n % 2", "1:10|0:20", "", true)]
    [InlineData("from int n in values group n * 10 by n % 2", "1:10|0:20", "", true)]
    [InlineData("from n in values.AsQueryable() group Element(n) by Key(n)", "1:10|0:20", "K1E1K2E2", true)]
    [InlineData("from n in values.AsQueryable() group Element(n).ToString().Length by Key(n)", "1:2|0:2", "K1E1K2E2", true)]
    [InlineData("from n in values group ThrowingElement(n) by Key(n)", "throws:InvalidOperationException", "K1E1K2E2", false)]
    [InlineData("from n in values group new Box(10).Item by n % 2", "1:10|0:10", "CC", true)]
    [InlineData("from n in values group new Box(n).Item by n % 2", "1:1|0:2", "CC", true)]
    [InlineData("from n in values group new Box(Element(n)).Item by Key(n)", "1:10|0:20", "K1E1CK2E2C", true)]
    [InlineData("from n in values group new Box(-1).Item by n % 2", "throws:InvalidOperationException", "C", true)]
    [InlineData("from n in values group values[n - 1] + 10 by n % 2", "1:11|0:12", "", true)]
    [InlineData("from n in values group values[3] + n by n % 2", "throws:IndexOutOfRangeException", "", true)]
    public void Grouping_PreservesElementsAndDeferredRepeatedEnumeration(
        string query, string expectedGroups, string tracePerEnumeration, bool preserved)
    {
        var expected = $"before=;first={expectedGroups}:{tracePerEnumeration};second={expectedGroups}:{tracePerEnumeration}{tracePerEnumeration}";
        AssertEquivalent(query, expected, preserved);
    }

    [Fact]
    public void Grouping_ReadsCapturedStateDuringEnumerationNotConstruction()
    {
        AssertEquivalent("from n in values group n * Factor by n % 2",
            "before=;first=1:20|0:40:;second=1:30|0:60:", preserved: false);
    }

    [Fact]
    public void Grouping_EvaluatesSourceOnceWhenQueryIsConstructed()
    {
        AssertEquivalent("from n in Source(values) group Element(n) by Key(n)",
            "before=S;first=1:10|0:20:SK1E1K2E2;second=1:10|0:20:SK1E1K2E2K1E1K2E2", preserved: false);
    }

    [Fact]
    public void Grouping_DoesNotSnapshotIndexedStateBeforeEnumeration()
    {
        AssertEquivalent("from n in values group values[0] + n by n % 2",
            "before=;first=0:20,12:;second=0:20,12:", preserved: true, mutateInput: true);
    }

    [Fact]
    public void MultipleInlineLambdaCallStatements_RemainABlockBody()
    {
        var diagnostics = new Calor.Compiler.Diagnostics.DiagnosticBag();
        var source = """
            §M{m1:InlineCalls}
              §F{f1:Build:pub} () -> void
                §B{action} §LAM{lam1} §C{First} §/C §C{Second} §/C §/LAM{lam1}
            """;
        var tokens = new Parsing.Lexer(source, diagnostics).TokenizeAll();
        var module = new Parsing.Parser(tokens, diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors));
        var bind = Assert.IsType<Ast.BindStatementNode>(module.Functions[0].Body[0]);
        var lambda = Assert.IsType<Ast.LambdaExpressionNode>(bind.Initializer);
        Assert.Null(lambda.ExpressionBody);
        Assert.Equal(2, lambda.StatementBody!.Count);
    }

    [Fact]
    public void DiscardedNonVoidCallLambda_RetainsActionRatherThanFunc()
    {
        const string source = """
            public static class Migrated
            {
                private static int Value() => 42;
                public static object Build()
                {
                    var action = () => { Value(); };
                    return action;
                }
            }
            """;
        var conversion = new CSharpToCalorConverter().Convert(source);
        Assert.True(conversion.Success, string.Join("; ", conversion.Issues.Select(issue => issue.Message)));
        var compiled = Program.Compile(conversion.CalorSource!, "action.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            EnforceEffects = true,
            StatusWriter = TextWriter.Null
        });
        Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics.Errors));
        Assert.IsType<Action>(Compile(source).GetMethod("Build")!.Invoke(null, null));
        Assert.IsType<Action>(Compile(compiled.GeneratedCode).GetMethod("Build")!.Invoke(null, null));
    }

    private static void AssertEquivalent(string query, string expected, bool preserved, bool mutateInput = false)
    {
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using System.Linq;
            public static class Migrated
            {
                public static string Trace = "";
                public static int Factor = 10;
                private static int[] Source(int[] values) { Trace += "S"; return values; }
                private class Box
                {
                    public int Item;
                    public Box(int number)
                    {
                        Trace += "C";
                        if (number < 0) throw new InvalidOperationException();
                        Item = number;
                    }
                }
                private static int Key(int value) { Trace += "K" + value; return value % 2; }
                private static int Element(int value) { Trace += "E" + value; return value * 10; }
                private static bool Pass(int value) { Trace += "W" + value; return true; }
                private static int ThrowingElement(int value)
                {
                    Trace += "E" + value;
                    if (value == 2) throw new InvalidOperationException();
                    return value * 10;
                }
                public static IEnumerable<IGrouping<int, int>> Build(int[] values) => {{query}};
            }
            """;
        var conversion = new CSharpToCalorConverter().Convert(source);
        Assert.True(conversion.Success, string.Join("; ", conversion.Issues.Select(issue => issue.Message))
            + "\n" + conversion.CalorSource);
        Assert.DoesNotContain(conversion.Losses, loss => loss.Kind == ConversionLossKind.Dropped);
        if (preserved)
            Assert.Contains(conversion.Losses, loss =>
                loss.Kind == ConversionLossKind.InteropPreserved && loss.Feature == "linq-query");
        else
        {
            Assert.Contains("GroupBy", conversion.CalorSource);
            Assert.DoesNotContain(conversion.Losses, loss =>
                loss.Kind == ConversionLossKind.InteropPreserved && loss.Feature == "linq-query");
        }
        var compiled = Program.Compile(conversion.CalorSource!, "grouping.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            EnforceEffects = true,
            StatusWriter = TextWriter.Null
        });
        Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics.Errors));
        Assert.Equal(expected, Observe(source, mutateInput));
        Assert.Equal(expected, Observe(compiled.GeneratedCode, mutateInput));
    }

    private static string Observe(string source, bool mutateInput)
    {
        var type = Compile(source);
        var values = new[] {1, 2};
        var query = (IEnumerable<IGrouping<int, int>>)type.GetMethod("Build")!
            .Invoke(null, [values])!;
        var trace = type.GetField("Trace")!;
        var before = trace.GetValue(null);
        if (mutateInput)
            values[0] = 10;
        type.GetField("Factor")!.SetValue(null, 20);
        var first = Enumerate(query) + ":" + trace.GetValue(null);
        type.GetField("Factor")!.SetValue(null, 30);
        var second = Enumerate(query) + ":" + trace.GetValue(null);
        return $"before={before};first={first};second={second}";
    }

    private static Type Compile(string source)
    {
        var compilation = CSharpCompilation.Create("GroupingOracle_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        Assert.True(emit.Success, string.Join("; ", emit.Diagnostics) + "\n" + source);
        return Assembly.Load(image.ToArray()).GetTypes().Single(type => type.Name == "Migrated");
    }

    private static string Enumerate(IEnumerable<IGrouping<int, int>> query)
    {
        try
        {
            return string.Join("|", query.Select(group => group.Key + ":" + string.Join(",", group)));
        }
        catch (Exception exception)
        {
            return "throws:" + exception.GetType().Name;
        }
    }
}
