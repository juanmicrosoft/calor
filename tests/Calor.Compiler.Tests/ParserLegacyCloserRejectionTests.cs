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
}
