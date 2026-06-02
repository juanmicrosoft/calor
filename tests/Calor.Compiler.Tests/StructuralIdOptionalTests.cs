using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for RFC §1 Phase 1: structural openers accept the bare-name form
/// (no surrounding ID block) in addition to the legacy
/// <c>§M{id:Name}</c> / <c>§F{id:Name:vis}</c> / <c>§L{id:var:from:to}</c>
/// forms.
/// </summary>
public class StructuralIdOptionalTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    [Fact]
    public void OptionalId_Module_BareName_Parses()
    {
        var source = """
            §M{Calculator}
            §F{Main:pub}
            §O{void}
            §R
            §/F
            §/M
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors,
            string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Equal("Calculator", module.Name);
        Assert.Contains("_auto", module.Id);
    }

    [Fact]
    public void OptionalId_Module_LegacyForm_StillParses()
    {
        var source = """
            §M{m001:Calculator}
            §F{Main:pub}
            §O{void}
            §R
            §/F
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors,
            string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Equal("Calculator", module.Name);
        Assert.Equal("m001", module.Id);
    }

    [Fact]
    public void OptionalId_Loop_BareForm_Parses()
    {
        // §L{var:from:to} — no leading id; bare numeric bounds so the
        // colon-based attribute splitter doesn't split typed literals.
        var source = """
            §M{Loops}
            §F{Sum:pub}
            §O{void}
            §B{~total:i32} INT:0
            §L{i:0:10}
              §B{~total:i32} (+ total i)
            §/L
            §R
            §/F
            §/M
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors,
            string.Join("\n", diagnostics.Select(d => d.Message)));
        var fn = Assert.Single(module.Functions);
        var loop = Assert.Single(fn.Body.OfType<ForStatementNode>());
        Assert.Equal("i", loop.VariableName);
        Assert.Contains("_auto", loop.Id);
    }

    [Fact]
    public void OptionalId_Loop_LegacyForm_StillParses()
    {
        var source = """
            §M{m001:Loops}
            §F{f001:Sum:pub}
            §O{void}
            §B{~total:i32} INT:0
            §L{l001:i:0:10}
              §B{~total:i32} (+ total i)
            §/L{l001}
            §R
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors,
            string.Join("\n", diagnostics.Select(d => d.Message)));
        var fn = Assert.Single(module.Functions);
        var loop = Assert.Single(fn.Body.OfType<ForStatementNode>());
        Assert.Equal("l001", loop.Id);
        Assert.Equal("i", loop.VariableName);
    }

    [Theory]
    [InlineData("m_01J5X7K9M2NPQRSTABWXYZ12AB", true)]     // ULID-shaped
    [InlineData("f_abc123def456", true)]                    // compact (12)
    [InlineData("ctor_01J5X7K9M2NPQRSTABWXYZ12AB", true)]   // ctor prefix
    [InlineData("Calculator", false)]                       // PascalCase name
    [InlineData("do_thing", false)]                         // user snake_case
    [InlineData("Foo_bar", false)]                          // mixed case
    [InlineData("", false)]
    [InlineData("f001", false)]                             // legacy short
    [InlineData("f_short", false)]                          // wrong payload len
    public void LooksLikeId_ClassifiesValuesCorrectly(string value, bool expected)
    {
        Assert.Equal(expected, AttributeHelper.LooksLikeId(value));
    }
}
