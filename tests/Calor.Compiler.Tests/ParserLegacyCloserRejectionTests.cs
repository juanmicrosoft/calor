// migrate_inline_calor: skip - fixture intentionally embeds closer-form Calor literals or uses position/template patterns incompatible with auto-migration.
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Phase 4c / 4d — verify the parser always rejects legacy structural
/// closing tags and emits <c>Calor0830 LegacyCloserForm</c>. Indent
/// form is now the only accepted surface; closer form was removed.
/// </summary>
public class ParserLegacyCloserRejectionTests
{
    private static (IList<Diagnostic> Diagnostics, bool HasErrors) Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        _ = parser.Parse();
        return (diagnostics.ToList(), diagnostics.HasErrors);
    }

    private static DiagnosticBag ParseBag(string source)
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        _ = parser.Parse();
        return diagnostics;
    }

    [Fact]
    public void IndentFormSource_NoLegacyDiagnostic()
    {
        const string src = """
            module Calc Calc block
              func Add (int:a, int:b) → int
                !
                return a + b
            """;
        var (diags, hasErrors) = Parse(src);
        Assert.False(hasErrors, "Indent-form source must parse cleanly");
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.LegacyCloserForm);
    }

    [Fact]
    public void RetainedCloser_DoWhile_NoDiagnostic()
    {
        // §/DO carries the do-while loop condition — retained.
        const string src = """
            §M{Calc}
              §F{Tick:pub}
                §O{void}
                §B{~i:i32} (INT:0)
                §DO
                  §= ~i (+ i (INT:1))
                §/DO (< i (INT:10))
            """;
        var (diags, _) = Parse(src);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.LegacyCloserForm);
    }

    /// <summary>
    /// Mirrors the line/column edit application used by the LSP code-action
    /// handler and the calor_check MCP tool (apply path): edits are applied
    /// bottom-up so earlier line numbers stay valid.
    /// </summary>
    private static string ApplyFixes(string source, DiagnosticBag bag)
    {
        var edits = bag.DiagnosticsWithFixes
            .SelectMany(d => d.Fix.Edits)
            .OrderByDescending(e => e.StartLine)
            .ThenByDescending(e => e.StartColumn)
            .ToList();

        var lines = source.Replace("\r\n", "\n").Split('\n');

        foreach (var edit in edits)
        {
            var startLine = edit.StartLine - 1;
            var startCol = edit.StartColumn - 1;
            var endLine = edit.EndLine - 1;
            var endCol = edit.EndColumn - 1;

            if (startLine < 0 || startLine >= lines.Length) continue;
            if (endLine < 0 || endLine >= lines.Length) continue;

            var beforeEdit = startCol >= 0 && startCol <= lines[startLine].Length
                ? lines[startLine][..startCol]
                : lines[startLine];
            var afterEdit = endCol >= 0 && endCol <= lines[endLine].Length
                ? lines[endLine][endCol..]
                : "";

            var newContent = beforeEdit + edit.NewText + afterEdit;
            var newLines = newContent.Split('\n');

            var lineList = lines.ToList();
            lineList.RemoveRange(startLine, endLine - startLine + 1);
            lineList.InsertRange(startLine, newLines);
            lines = lineList.ToArray();
        }

        return string.Join('\n', lines);
    }
}
