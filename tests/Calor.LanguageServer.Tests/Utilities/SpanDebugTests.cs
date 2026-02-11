using Calor.Compiler;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.LanguageServer.Tests.Helpers;
using Calor.LanguageServer.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Calor.LanguageServer.Tests.Utilities;

public class SpanDebugTests
{
    private readonly ITestOutputHelper _output;

    public SpanDebugTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Debug_SpanPositions()
    {
        var source = @"§M{m001:TestModule}
§F{f001:Add}
§I{i32:a}
§O{i32}
§R a
§/F{f001}
§/M{m001}";

        _output.WriteLine("Source:");
        _output.WriteLine(source);
        _output.WriteLine("");

        // Show character positions
        _output.WriteLine("Character positions:");
        int line = 1, col = 1;
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == '\n')
            {
                _output.WriteLine($"  Offset {i,3}: newline (Line {line})");
                line++;
                col = 1;
            }
            else
            {
                _output.WriteLine($"  Offset {i,3}: '{source[i]}' at Line {line}, Col {col}");
                col++;
            }
        }
        _output.WriteLine("");

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        _output.WriteLine($"Module '{ast.Name}' span: Start={ast.Span.Start}, End={ast.Span.End}, Line={ast.Span.Line}, Col={ast.Span.Column}");
        _output.WriteLine("");

        if (ast.Functions.Count > 0)
        {
            var func = ast.Functions[0];
            _output.WriteLine($"Function '{func.Name}' span: Start={func.Span.Start}, End={func.Span.End}, Line={func.Span.Line}, Col={func.Span.Column}");

            foreach (var param in func.Parameters)
            {
                _output.WriteLine($"  Param '{param.Name}' type='{param.TypeName}' span: Start={param.Span.Start}, End={param.Span.End}, Line={param.Span.Line}, Col={param.Span.Column}");
            }

            foreach (var stmt in func.Body)
            {
                _output.WriteLine($"  Body stmt span: Start={stmt.Span.Start}, End={stmt.Span.End}, Line={stmt.Span.Line}, Col={stmt.Span.Column}, Type={stmt.GetType().Name}");
            }
        }

        _output.WriteLine("");

        // Now test FindSymbol at various positions
        _output.WriteLine("FindSymbol tests:");

        // Test at offset that should be in the function name area
        var funcLineOffset = source.IndexOf("§F{f001:Add}");
        _output.WriteLine($"Function line starts at offset {funcLineOffset}");

        // Try finding at line 2 (where function is)
        var result = LspTestHarness.FindSymbol(source, 2, 10);
        _output.WriteLine($"FindSymbol(line=2, col=10): {(result == null ? "null" : $"Name={result.Name}, Kind={result.Kind}")}");

        // Try at parameter line
        result = LspTestHarness.FindSymbol(source, 3, 5);
        _output.WriteLine($"FindSymbol(line=3, col=5): {(result == null ? "null" : $"Name={result.Name}, Kind={result.Kind}")}");

        // Calculate what offset line 2, col 10 maps to
        int targetOffset = LspTestHarness.GetOffset(source, 2, 10);
        _output.WriteLine($"Line 2, Col 10 = offset {targetOffset}, char = '{(targetOffset < source.Length ? source[targetOffset].ToString() : "EOF")}'");

        targetOffset = LspTestHarness.GetOffset(source, 3, 5);
        _output.WriteLine($"Line 3, Col 5 = offset {targetOffset}, char = '{(targetOffset < source.Length ? source[targetOffset].ToString() : "EOF")}'");
    }

    [Fact]
    public void Position_Offset_Conversion_IsCorrect()
    {
        var source = "Line1\nLine2\nLine3";

        // Line 1, Col 1 should be offset 0
        Assert.Equal(0, LspTestHarness.GetOffset(source, 1, 1));

        // Line 1, Col 3 should be offset 2 (0-indexed char 2 = 'n')
        Assert.Equal(2, LspTestHarness.GetOffset(source, 1, 3));

        // Line 2, Col 1 should be offset 6 (after "Line1\n")
        Assert.Equal(6, LspTestHarness.GetOffset(source, 2, 1));

        // Line 2, Col 3 should be offset 8
        Assert.Equal(8, LspTestHarness.GetOffset(source, 2, 3));

        // Line 3, Col 1 should be offset 12 (after "Line1\nLine2\n")
        Assert.Equal(12, LspTestHarness.GetOffset(source, 3, 1));
    }

    [Fact]
    public void Span_Contains_Position()
    {
        var source = @"§M{m001:TestModule}
§F{f001:Add}
§I{i32:a}
§O{i32}
§R a
§/F{f001}
§/M{m001}";

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var func = ast.Functions[0];

        _output.WriteLine($"Function span: {func.Span.Start} to {func.Span.End}");

        // Get offset for line 2 (function line)
        var offset = LspTestHarness.GetOffset(source, 2, 5);
        _output.WriteLine($"Line 2, Col 5 = offset {offset}");
        _output.WriteLine($"Function.Span.Contains({offset}) = {func.Span.Contains(offset)}");

        // Get offset for line 3 (parameter line)
        offset = LspTestHarness.GetOffset(source, 3, 5);
        _output.WriteLine($"Line 3, Col 5 = offset {offset}");
        _output.WriteLine($"Function.Span.Contains({offset}) = {func.Span.Contains(offset)}");

        if (func.Parameters.Count > 0)
        {
            var param = func.Parameters[0];
            _output.WriteLine($"Param span: {param.Span.Start} to {param.Span.End}");
            _output.WriteLine($"Param.Span.Contains({offset}) = {param.Span.Contains(offset)}");
        }
    }
}
