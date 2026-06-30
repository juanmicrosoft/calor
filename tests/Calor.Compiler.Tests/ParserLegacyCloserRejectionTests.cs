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
            §M{m1:Calc}
              §F{f1:Add:pub}
                §I{i32:a}
                §I{i32:b}
                §O{i32}
                §R (+ a b)
            """;
        var (diags, hasErrors) = Parse(src);
        Assert.False(hasErrors, "Indent-form source must parse cleanly");
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.LegacyCloserForm);
    }

    [Fact]
    public void LegacyCloserFunction_EmitsCalor0830()
    {
        const string src = """
            §M{m1:Calc}
              §F{f1:Add:pub}
                §I{i32:a}
                §I{i32:b}
                §O{i32}
                §R (+ a b)
              §/F{f1}
            §/M{m1}
            """;
        var (diags, hasErrors) = Parse(src);
        Assert.True(hasErrors, "Source with legacy structural closers must error");
        var legacy = diags.Where(d => d.Code == DiagnosticCode.LegacyCloserForm).ToList();
        Assert.NotEmpty(legacy);
        Assert.Contains(legacy, d => d.Message.Contains("§/F", StringComparison.Ordinal));
        Assert.Contains(legacy, d => d.Message.Contains("§/M", StringComparison.Ordinal));
    }

    [Fact]
    public void RetainedCloser_DoWhile_NoDiagnostic()
    {
        // §/DO carries the do-while loop condition — retained.
        const string src = """
            §M{m1:Calc}
              §F{f1:Tick:pub}
                §O{void}
                §B{~i:i32} (INT:0)
                §DO{d1}
                  §= ~i (+ i (INT:1))
                §/DO{d1} (< i (INT:10))
            """;
        var (diags, _) = Parse(src);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.LegacyCloserForm);
    }

    [Fact]
    public void LegacyCloser_Message_DoesNotRecommendCalorFormat()
    {
        // calor format / calor lint --fix parse first and abort on
        // Calor0830, so they cannot heal closer-form. The message must
        // not send users to a command that can't fix the file.
        const string src = """
            §M{m1:Calc}
              §F{f1:Add:pub}
                §O{i32}
                §R (INT:1)
              §/F{f1}
            §/M{m1}
            """;
        var (diags, _) = Parse(src);
        var legacy = diags.Where(d => d.Code == DiagnosticCode.LegacyCloserForm).ToList();
        Assert.NotEmpty(legacy);
        Assert.All(legacy, d =>
            Assert.DoesNotContain("calor format", d.Message, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(legacy, d => d.Message.Contains("delete the closer line", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LegacyCloser_CarriesSuggestedFix_ThatDeletesTheCloser()
    {
        const string src = """
            §M{m1:Calc}
              §F{f1:Add:pub}
                §O{i32}
                §R (INT:1)
              §/F{f1}
            §/M{m1}
            """;
        var bag = ParseBag(src);

        var fixes = bag.DiagnosticsWithFixes
            .Where(d => d.Code == DiagnosticCode.LegacyCloserForm)
            .ToList();

        // One fix per structural closer (§/F and §/M).
        Assert.Equal(2, fixes.Count);
        Assert.All(fixes, dwf =>
        {
            var edit = Assert.Single(dwf.Fix.Edits);
            // Deletion: empty replacement that starts at column 1 of the closer line.
            Assert.Equal(string.Empty, edit.NewText);
            Assert.Equal(1, edit.StartColumn);
            // The removal range covers the closer keyword and its {id} payload.
            Assert.True(edit.EndColumn > edit.StartColumn);
        });
    }

    [Fact]
    public void ApplyingLegacyCloserFix_YieldsCleanIndentOnlySource()
    {
        const string src = """
            §M{m1:Calc}
              §F{f1:Add:pub}
                §I{i32:a}
                §I{i32:b}
                §O{i32}
                §R (+ a b)
              §/F{f1}
            §/M{m1}
            """;
        var bag = ParseBag(src);
        var healed = ApplyFixes(src, bag);

        Assert.DoesNotContain("§/", healed);

        var (diags, hasErrors) = Parse(healed);
        Assert.False(hasErrors, "Healed source must parse cleanly");
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
