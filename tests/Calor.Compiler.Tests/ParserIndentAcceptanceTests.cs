using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for the Phase 1 indent-aware Parser (RFC §4.2). These exercise the
/// IsBlockEnd / ExpectBlockEnd helpers in Parser.cs that allow blocks to be
/// terminated by either an explicit closer (§/F etc.) or a Dedent emitted by
/// <see cref="Lexer.TokenizeWithIndent"/>.
///
/// Phase 1 scope: parser ACCEPTS indent form additively. The lexer default
/// is still closer-only (<see cref="Lexer.Tokenize"/>) so existing fixtures
/// are unaffected. The full indent-only flip happens in Phase 2+.
/// </summary>
public class ParserIndentAcceptanceTests
{
    private static ModuleNode ParseIndentForm(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        // Strip Newlines and Indents — TokenizeWithIndent includes them but
        // the Parser grammar is newline-insensitive (§ markers delimit
        // statements) and uses Dedent as the only structural block-end
        // signal. Indents are decorative since every block-opener (§F, §M,
        // §IF, etc.) is already an explicit tag.
        var tokens = lexer.TokenizeWithIndentAll()
            .Where(t => t.Kind != TokenKind.Newline && t.Kind != TokenKind.Indent)
            .ToList();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static ModuleNode ParseCloserForm(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var parser = new Parser(lexer.TokenizeAll(), diagnostics);
        return parser.Parse();
    }

    [Fact]
    public void IndentMode_EmptyModuleNoCloser_Parses()
    {
        // Indent form: no §/M, dedent at EOF terminates the module.
        var src = "§M{m1:Test}\n";
        var module = ParseIndentForm(src, out var diag);

        Assert.False(diag.HasErrors, string.Join(", ", diag.Errors.Select(d => d.Message)));
        Assert.Equal("Test", module.Name);
    }

    [Fact]
    public void IndentMode_ModuleWithFunction_Parses()
    {
        var src = "§M{m1:Test}\n" +
                  "  §F{f1:Add:pub}\n" +
                  "    §I{i32:a}\n" +
                  "    §I{i32:b}\n" +
                  "    §O{i32}\n" +
                  "    §R (+ a b)\n";
        var module = ParseIndentForm(src, out var diag);

        Assert.False(diag.HasErrors, string.Join(", ", diag.Errors.Select(d => d.Message)));
        Assert.Equal("Test", module.Name);
        Assert.Single(module.Functions);
        Assert.Equal("Add", module.Functions[0].Name);
    }

    [Fact]
    public void IndentMode_NestedIfChain_Parses()
    {
        var src = "§M{m1:Test}\n" +
                  "  §F{f1:Sign:pub}\n" +
                  "    §I{i32:n}\n" +
                  "    §O{i32}\n" +
                  "    §IF{i1} (> n 0)\n" +
                  "      §R 1\n" +
                  "    §EI (< n 0)\n" +
                  "      §R -1\n" +
                  "    §EL\n" +
                  "      §R 0\n";
        var module = ParseIndentForm(src, out var diag);

        Assert.False(diag.HasErrors, string.Join(", ", diag.Errors.Select(d => d.Message)));
        Assert.Single(module.Functions);
    }

    [Fact]
    public void CloserMode_ExistingFixture_StillParses()
    {
        // Regression guard: closer form continues to parse unchanged.
        var src = "§M{m1:Test}\n" +
                  "§F{f1:Add:pub}\n" +
                  "§I{i32:a}\n" +
                  "§I{i32:b}\n" +
                  "§O{i32}\n" +
                  "§R (+ a b)\n" +
                  "§/F{f1}\n" +
                  "§/M{m1}\n";
        var module = ParseCloserForm(src, out var diag);

        Assert.False(diag.HasErrors, string.Join(", ", diag.Errors.Select(d => d.Message)));
        Assert.Equal("Test", module.Name);
        Assert.Single(module.Functions);
    }

    [Fact]
    public void IndentMode_ClosersStillAccepted_AdditiveBehavior()
    {
        // Closer-form source parsed through TokenizeWithIndent should still
        // work. The lexer emits Dedent before §/F, then §/F, then Dedent
        // before §/M, then §/M. ExpectBlockEnd consumes both forms.
        var src = "§M{m1:Test}\n" +
                  "  §F{f1:Add:pub}\n" +
                  "    §I{i32:a}\n" +
                  "    §I{i32:b}\n" +
                  "    §O{i32}\n" +
                  "    §R (+ a b)\n" +
                  "  §/F{f1}\n" +
                  "§/M{m1}\n";
        var module = ParseIndentForm(src, out var diag);

        Assert.False(diag.HasErrors, string.Join(", ", diag.Errors.Select(d => d.Message)));
        Assert.Equal("Test", module.Name);
        Assert.Single(module.Functions);
    }

    [Fact]
    public void IndentMode_FullPipelineToCSharp_Emits()
    {
        // E2E pipeline test: Lexer (indent mode) → Parser → CSharpEmitter.
        // Validates that indent-form source flows all the way through the
        // compiler to a runnable C# string, with no explicit closers.
        var src = "§M{m1:Mathy}\n" +
                  "  §F{f1:Add:pub}\n" +
                  "    §I{i32:a}\n" +
                  "    §I{i32:b}\n" +
                  "    §O{i32}\n" +
                  "    §R (+ a b)\n";

        var module = ParseIndentForm(src, out var diag);
        Assert.False(diag.HasErrors, string.Join(", ", diag.Errors.Select(d => d.Message)));

        var emitter = new Calor.Compiler.CodeGen.CSharpEmitter();
        var csharp = emitter.Emit(module);

        Assert.Contains("Add", csharp);
        Assert.Contains("int", csharp);
        Assert.Contains("a + b", csharp);
    }

    [Fact]
    public void IndentMode_FullPipelineWithIfChain_Emits()
    {
        // E2E pipeline test on indent-form code with an §IF/§EI/§EL chain.
        var src = "§M{m1:Sign}\n" +
                  "  §F{f1:Of:pub}\n" +
                  "    §I{i32:n}\n" +
                  "    §O{i32}\n" +
                  "    §IF{i1} (> n 0)\n" +
                  "      §R 1\n" +
                  "    §EI (< n 0)\n" +
                  "      §R -1\n" +
                  "    §EL\n" +
                  "      §R 0\n";

        var module = ParseIndentForm(src, out var diag);
        Assert.False(diag.HasErrors, string.Join(", ", diag.Errors.Select(d => d.Message)));

        var emitter = new Calor.Compiler.CodeGen.CSharpEmitter();
        var csharp = emitter.Emit(module);

        Assert.Contains("Of", csharp);
        Assert.Contains("if", csharp);
        Assert.Contains("else", csharp);
    }

    [Fact]
    public void CloserMode_FixtureParsesViaTokenizeAllForParser()
    {
        // Phase 1b regression guard: real-world closer-form fixture parses
        // correctly when fed through the new indent-aware production entry
        // point (TokenizeAllForParser). This proves we can flip all 16 CLI
        // callers from TokenizeAll → TokenizeAllForParser without breaking
        // existing closer-only source.
        var src = "§M{m001:FizzBuzz}\n" +
                  "§F{f001:Main:pub}\n" +
                  "  §O{void}\n" +
                  "  §E{cw}\n" +
                  "  §L{for1:i:1:100:1}\n" +
                  "    §IF{if1} (== (% i 15) 0) → §P \"FizzBuzz\"\n" +
                  "    §EI (== (% i 3) 0) → §P \"Fizz\"\n" +
                  "    §EI (== (% i 5) 0) → §P \"Buzz\"\n" +
                  "    §EL → §P i\n" +
                  "    §/I{if1}\n" +
                  "  §/L{for1}\n" +
                  "§/F{f001}\n" +
                  "§/M{m001}\n";

        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(src, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Errors.Select(d => d.Message)));
        Assert.Equal("FizzBuzz", module.Name);
        Assert.Single(module.Functions);

        var emitter = new Calor.Compiler.CodeGen.CSharpEmitter();
        var csharp = emitter.Emit(module);
        Assert.Contains("FizzBuzz", csharp);
        Assert.Contains("Main", csharp);
    }
}
