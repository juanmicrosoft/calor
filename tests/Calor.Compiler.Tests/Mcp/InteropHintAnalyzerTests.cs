using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

public class InteropHintAnalyzerTests
{
    [Fact]
    public void AnalyzeInteropBlocks_WithNoInteropBlocks_ReturnsEmpty()
    {
        var calorSource = """
            §M{m1:TestModule}
            §CL{c1:Calculator:pub}
              §MT{m1:Add:pub}
                §I{i32:a}
                §I{i32:b}
                §O{i32}
                §R a + b §/R
              §/MT
            §/CL
            §/M
            """;

        var hints = InteropHintAnalyzer.AnalyzeInteropBlocks(calorSource);

        Assert.Empty(hints);
    }

    [Fact]
    public void AnalyzeInteropBlocks_WithForeachInInterop_ReturnsHint()
    {
        var calorSource = "§CSHARP{foreach (var item in items) { Process(item); }}§/CSHARP";

        var hints = InteropHintAnalyzer.AnalyzeInteropBlocks(calorSource);

        Assert.Single(hints);
        Assert.Contains("foreach", hints[0]);
        Assert.Contains("§L{item:collection}", hints[0]);
    }

    [Fact]
    public void AnalyzeInteropBlocks_WithSwitchInInterop_ReturnsHint()
    {
        var calorSource = "§CSHARP{switch (value) { case 1: return \"one\"; default: return \"other\"; }}§/CSHARP";

        var hints = InteropHintAnalyzer.AnalyzeInteropBlocks(calorSource);

        Assert.Single(hints);
        Assert.Contains("switch", hints[0]);
    }

    [Fact]
    public void AnalyzeInteropBlocks_WithAsyncInInterop_ReturnsHint()
    {
        var calorSource = "§CSHARP{async Task<string> FetchAsync() { return await client.GetStringAsync(url); }}§/CSHARP";

        var hints = InteropHintAnalyzer.AnalyzeInteropBlocks(calorSource);

        Assert.Contains(hints, h => h.Contains("async"));
    }

    [Fact]
    public void AnalyzeInteropBlocks_WithYieldInInterop_ReturnsHint()
    {
        var calorSource = "§CSHARP{public IEnumerable<int> GetNumbers() { foreach (var item in items) { yield return item * 2; } }}§/CSHARP";

        var hints = InteropHintAnalyzer.AnalyzeInteropBlocks(calorSource);

        Assert.Contains(hints, h => h.Contains("yield"));
        Assert.Contains(hints, h => h.Contains("§YIELD"));
    }

    [Fact]
    public void AnalyzeInteropBlocks_WithMultipleFeatures_ReturnsDeduplicatedHints()
    {
        var calorSource =
            "§CSHARP{foreach (var item in items) { Process(item); }\nforeach (var other in others) { Handle(other); }}§/CSHARP\n" +
            "§CSHARP{switch (x) { case 1: break; }}§/CSHARP";

        var hints = InteropHintAnalyzer.AnalyzeInteropBlocks(calorSource);

        // foreach should appear only once despite two occurrences
        var foreachHints = hints.Count(h => h.Contains("foreach"));
        Assert.Equal(1, foreachHints);

        // switch should also appear
        Assert.Contains(hints, h => h.Contains("switch"));
    }

    [Fact]
    public void AnalyzeInteropBlocks_WithPreprocessorInInterop_ReturnsHint()
    {
        var calorSource = "§CSHARP{#if DEBUG\nConsole.WriteLine(\"debug\");\n#endif}§/CSHARP";

        var hints = InteropHintAnalyzer.AnalyzeInteropBlocks(calorSource);

        Assert.Contains(hints, h => h.Contains("preprocessor"));
        Assert.Contains(hints, h => h.Contains("§PP"));
    }

    [Fact]
    public void AnalyzeInteropBlocks_WithEventInInterop_ReturnsHint()
    {
        var calorSource = "§CSHARP{public event EventHandler OnChanged;}§/CSHARP";

        var hints = InteropHintAnalyzer.AnalyzeInteropBlocks(calorSource);

        Assert.Contains(hints, h => h.Contains("event"));
        Assert.Contains(hints, h => h.Contains("§EVT"));
    }

    [Fact]
    public void AnalyzeInteropBlocks_WithOperatorInInterop_ReturnsHint()
    {
        var calorSource = "§CSHARP{public static MyType operator +(MyType a, MyType b) { return new MyType(a.Value + b.Value); }}§/CSHARP";

        var hints = InteropHintAnalyzer.AnalyzeInteropBlocks(calorSource);

        Assert.Contains(hints, h => h.Contains("operator"));
        Assert.Contains(hints, h => h.Contains("§OP"));
    }

    [Fact]
    public void AnalyzeInteropBlocks_WithImplicitOperator_ReturnsSingleHint()
    {
        // implicit operator should produce exactly one hint (not two overlapping hints)
        var calorSource = "§CSHARP{public static implicit operator int(MyType value) { return value.Value; }}§/CSHARP";

        var hints = InteropHintAnalyzer.AnalyzeInteropBlocks(calorSource);

        var operatorHints = hints.Count(h => h.Contains("operator"));
        Assert.Equal(1, operatorHints);
        Assert.Contains(hints, h => h.Contains("implicit"));
    }

    [Fact]
    public void AnalyzeInteropBlocks_WithNullSource_ReturnsEmpty()
    {
        var hints = InteropHintAnalyzer.AnalyzeInteropBlocks(null);

        Assert.Empty(hints);
    }

    [Fact]
    public void AnalyzeInteropBlocks_WithEmptySource_ReturnsEmpty()
    {
        var hints = InteropHintAnalyzer.AnalyzeInteropBlocks("");

        Assert.Empty(hints);
    }

    [Fact]
    public void AnalyzeInteropBlocks_WithUsingStatementInInterop_ReturnsHint()
    {
        var calorSource = "§CSHARP{using (var stream = File.OpenRead(path)) { Process(stream); }}§/CSHARP";

        var hints = InteropHintAnalyzer.AnalyzeInteropBlocks(calorSource);

        Assert.Contains(hints, h => h.Contains("using"));
        Assert.Contains(hints, h => h.Contains("§USE"));
    }

    [Fact]
    public void AnalyzeInteropBlocks_WithDelegateInInterop_ReturnsHint()
    {
        var calorSource = "§CSHARP{public delegate void Callback(string message);}§/CSHARP";

        var hints = InteropHintAnalyzer.AnalyzeInteropBlocks(calorSource);

        Assert.Contains(hints, h => h.Contains("delegate"));
        Assert.Contains(hints, h => h.Contains("§DEL"));
    }

    [Fact]
    public void AnalyzeInteropBlocks_WithStructInInterop_ReturnsHint()
    {
        var calorSource = "§CSHARP{public struct Point { public int X; public int Y; }}§/CSHARP";

        var hints = InteropHintAnalyzer.AnalyzeInteropBlocks(calorSource);

        Assert.Contains(hints, h => h.Contains("struct"));
    }

    [Fact]
    public void AnalyzeInteropBlocks_WithGenericConstraintInInterop_ReturnsHint()
    {
        var calorSource = "§CSHARP{public class Repo<T> where T : class, IEntity { }}§/CSHARP";

        var hints = InteropHintAnalyzer.AnalyzeInteropBlocks(calorSource);

        Assert.Contains(hints, h => h.Contains("§WHERE"));
    }

    [Fact]
    public void AnalyzeInteropBlocks_ForeachOutsideInterop_NotDetected()
    {
        // foreach keyword outside §CSHARP blocks should NOT generate hints
        var calorSource = """
            §L{item:items}
              §!{Process}(item)§/!
            §/L
            """;

        var hints = InteropHintAnalyzer.AnalyzeInteropBlocks(calorSource);

        Assert.Empty(hints);
    }

    [Fact]
    public void AnalyzeInteropBlocks_MultipleBlocks_AnalyzesAll()
    {
        var calorSource =
            "§CL{c1:Test:pub}\n" +
            "§CSHARP{foreach (var item in items) { }}§/CSHARP\n" +
            "§MT{m1:Foo:pub}\n  §O{void}\n§/MT\n" +
            "§CSHARP{switch (x) { case 1: break; }}§/CSHARP\n" +
            "§/CL";

        var hints = InteropHintAnalyzer.AnalyzeInteropBlocks(calorSource);

        Assert.Contains(hints, h => h.Contains("foreach"));
        Assert.Contains(hints, h => h.Contains("switch"));
    }

    [Fact]
    public void AnalyzeInteropBlocks_MultilineContent_MatchesCorrectly()
    {
        // Real-world format: multi-line content inside braces
        var calorSource = "§CSHARP{\npublic IEnumerable<int> GetNumbers()\n{\n    for (int i = 0; i < 10; i++)\n        yield return i;\n}}§/CSHARP";

        var hints = InteropHintAnalyzer.AnalyzeInteropBlocks(calorSource);

        Assert.Contains(hints, h => h.Contains("yield"));
    }

    [Fact]
    public void AnalyzeInteropBlocks_NestedBracesInContent_ExtractsCorrectly()
    {
        // Content with nested braces — the regex should match from §CSHARP{ to the
        // first }§/CSHARP. The Lexer uses the same end-marker strategy.
        var calorSource = "§CSHARP{public void Foo() { if (true) { Bar(); } }}§/CSHARP";

        // This should not crash and should extract content
        var hints = InteropHintAnalyzer.AnalyzeInteropBlocks(calorSource);

        // No specific C# feature keywords to detect in this case, just verify it doesn't crash
        Assert.NotNull(hints);
    }
}
