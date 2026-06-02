using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Phase 4c — verify the parser's <c>rejectLegacyClosers</c> strict mode
/// emits <c>Calor0830 LegacyCloserForm</c> when it encounters legacy
/// structural closing tags, and stays silent on indent-form sources.
///
/// The lax default path (no <c>rejectLegacyClosers</c> arg) must still
/// accept both forms so existing tooling, MCP tools, LSP, and the
/// MSBuild task keep working until the cross-surface migration is
/// complete.
/// </summary>
public class ParserLegacyCloserRejectionTests
{
    private static (IList<Diagnostic> Diagnostics, bool HasErrors) Parse(string source, bool rejectLegacyClosers)
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics, rejectLegacyClosers);
        _ = parser.Parse();
        return (diagnostics.ToList(), diagnostics.HasErrors);
    }

    [Fact]
    public void StrictMode_IndentFormSource_NoLegacyDiagnostic()
    {
        const string src = """
            §M{m1:Calc}
              §F{f1:Add:pub}
                §I{i32:a}
                §I{i32:b}
                §O{i32}
                §R (+ a b)
            """;
        var (diags, hasErrors) = Parse(src, rejectLegacyClosers: true);
        Assert.False(hasErrors, "Indent-form source must parse cleanly under strict mode");
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.LegacyCloserForm);
    }

    [Fact]
    public void StrictMode_LegacyCloserFunction_EmitsCalor0830()
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
        var (diags, hasErrors) = Parse(src, rejectLegacyClosers: true);
        Assert.True(hasErrors, "Source with legacy structural closers must error under strict mode");
        var legacy = diags.Where(d => d.Code == DiagnosticCode.LegacyCloserForm).ToList();
        Assert.NotEmpty(legacy);
        Assert.Contains(legacy, d => d.Message.Contains("§/F", StringComparison.Ordinal));
        Assert.Contains(legacy, d => d.Message.Contains("§/M", StringComparison.Ordinal));
    }

    [Fact]
    public void LaxMode_LegacyCloserSource_NoLegacyDiagnostic()
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
        var (diags, _) = Parse(src, rejectLegacyClosers: false);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.LegacyCloserForm);
    }

    [Fact]
    public void StrictMode_RetainedCloser_DoWhile_NoDiagnostic()
    {
        // §/DO carries the do-while loop condition — Phase 4c keeps it.
        const string src = """
            §M{m1:Calc}
              §F{f1:Tick:pub}
                §O{void}
                §B{~i:i32} (INT:0)
                §DO{d1}
                  §= ~i (+ i (INT:1))
                §/DO{d1} (< i (INT:10))
            """;
        var (diags, _) = Parse(src, rejectLegacyClosers: true);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.LegacyCloserForm);
    }
}
