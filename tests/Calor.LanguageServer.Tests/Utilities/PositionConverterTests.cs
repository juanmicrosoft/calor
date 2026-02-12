using Calor.Compiler.Parsing;
using Calor.LanguageServer.Utilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Xunit;

namespace Calor.LanguageServer.Tests.Utilities;

public class PositionConverterTests
{
    [Fact]
    public void ToCalorPosition_ZeroBased_ReturnsOneBased()
    {
        var position = new Position(0, 0);

        var (line, column) = PositionConverter.ToCalorPosition(position);

        Assert.Equal(1, line);
        Assert.Equal(1, column);
    }

    [Fact]
    public void ToCalorPosition_NonZero_ReturnsOneBased()
    {
        var position = new Position(5, 10);

        var (line, column) = PositionConverter.ToCalorPosition(position);

        Assert.Equal(6, line);
        Assert.Equal(11, column);
    }

    [Fact]
    public void ToOffset_FirstLine_ReturnsCorrectOffset()
    {
        var source = "hello world";
        var position = new Position(0, 6);

        var offset = PositionConverter.ToOffset(position, source);

        Assert.Equal(6, offset);
    }

    [Fact]
    public void ToOffset_SecondLine_ReturnsCorrectOffset()
    {
        var source = "hello\nworld";
        var position = new Position(1, 0);

        var offset = PositionConverter.ToOffset(position, source);

        Assert.Equal(6, offset);
    }

    [Fact]
    public void ToOffset_MultipleLines_ReturnsCorrectOffset()
    {
        var source = "line1\nline2\nline3";
        var position = new Position(2, 2);

        var offset = PositionConverter.ToOffset(position, source);

        Assert.Equal(14, offset); // "line1\nline2\n" = 12, then 2 more
    }

    [Fact]
    public void ToOffset_BeyondSource_ReturnsSourceLength()
    {
        var source = "short";
        var position = new Position(10, 0);

        var offset = PositionConverter.ToOffset(position, source);

        Assert.Equal(source.Length, offset);
    }

    [Fact]
    public void ToLspRange_SingleLineSpan_ReturnsCorrectRange()
    {
        var source = "hello world";
        var span = new TextSpan(0, 5, 1, 1); // "hello"

        var range = PositionConverter.ToLspRange(span, source);

        Assert.Equal(0, range.Start.Line);
        Assert.Equal(0, range.Start.Character);
        Assert.Equal(0, range.End.Line);
        Assert.Equal(5, range.End.Character);
    }

    [Fact]
    public void ToLspRange_MultiLineSpan_ReturnsCorrectRange()
    {
        var source = "hello\nworld";
        var span = new TextSpan(0, 11, 1, 1); // entire source

        var range = PositionConverter.ToLspRange(span, source);

        Assert.Equal(0, range.Start.Line);
        Assert.Equal(0, range.Start.Character);
        Assert.Equal(1, range.End.Line);
        Assert.Equal(5, range.End.Character);
    }

    [Fact]
    public void ToLspRangeSingleLine_ReturnsCorrectRange()
    {
        var span = new TextSpan(10, 5, 3, 8); // Line 3, column 8, length 5

        var range = PositionConverter.ToLspRangeSingleLine(span);

        Assert.Equal(2, range.Start.Line);     // 0-based: 3-1
        Assert.Equal(7, range.Start.Character); // 0-based: 8-1
        Assert.Equal(2, range.End.Line);
        Assert.Equal(12, range.End.Character); // 7 + 5
    }

    [Fact]
    public void FromLspPosition_ReturnsCorrectSpan()
    {
        var source = "hello\nworld";
        var position = new Position(1, 2);

        var span = PositionConverter.FromLspPosition(position, source);

        Assert.Equal(8, span.Start); // "hello\n" = 6, + 2
        Assert.Equal(0, span.Length);
        Assert.Equal(2, span.Line);
        Assert.Equal(3, span.Column);
    }

    [Fact]
    public void ToOffset_EmptySource_ReturnsZero()
    {
        var source = "";
        var position = new Position(0, 0);

        var offset = PositionConverter.ToOffset(position, source);

        Assert.Equal(0, offset);
    }

    [Fact]
    public void ToLspRange_EmptySpan_ReturnsZeroLengthRange()
    {
        var source = "hello world";
        var span = new TextSpan(5, 0, 1, 6); // zero-length at position 5

        var range = PositionConverter.ToLspRange(span, source);

        Assert.Equal(range.Start.Line, range.End.Line);
        Assert.Equal(range.Start.Character, range.End.Character);
    }
}
