using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests.Mcp;

public class InteropHintAnalyzerTests
{
    [Fact]
    public void AnalyzeInteropBlocks_WithNoInteropBlocks_ReturnsEmpty()
    {
        var calorSource = """
            §M{TestModule}
            §CL{Calculator:pub}
              §MT{Add:pub}
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
