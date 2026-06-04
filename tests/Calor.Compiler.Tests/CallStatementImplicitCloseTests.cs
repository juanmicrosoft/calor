using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Characterization tests pinning <c>ParseCallStatement</c>'s implicit-close
/// behavior at <c>Parser.cs:1374-1382</c>. The v0.6 call-elision RFC
/// (<c>docs/plans/v0.6-call-closer-elision.md</c>) generalizes this
/// statement-only behavior to expression-context calls; these tests pin the
/// current statement-only baseline before that change is implemented.
/// </summary>
public class CallStatementImplicitCloseTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static CallStatementNode FirstCall(ModuleNode module)
    {
        var func = module.Functions.First();
        return func.Body.OfType<CallStatementNode>().First();
    }

    [Fact]
    public void StatementContext_OneArg_NoAMarker_NoCloser_Parses()
    {
        // The implicit-close form: §C{Target} expr (no §A, no §/C)
        // is legal for STATEMENT context per Parser.cs:1374-1382.
        var source = """
§M{m001:Test}
  §F{f001:Foo:pub}
      §C{Console.WriteLine} STR:"hello"
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var call = FirstCall(module);
        Assert.Equal("Console.WriteLine", call.Target);
        Assert.Single(call.Arguments);
        Assert.IsType<StringLiteralNode>(call.Arguments[0]);
    }

    [Fact]
    public void StatementContext_OneArg_WithAMarker_ExplicitCloser_Parses()
    {
        // The canonical form: §C{Target} §A expr §/C
        var source = """
§M{m001:Test}
  §F{f001:Foo:pub}
      §C{Console.WriteLine} §A STR:"hello" §/C
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var call = FirstCall(module);
        Assert.Equal("Console.WriteLine", call.Target);
        Assert.Single(call.Arguments);
    }

    [Fact]
    public void StatementContext_ZeroArg_ExplicitCloser_Parses()
    {
        // Zero-arg form requires §/C today.
        var source = """
§M{m001:Test}
  §F{f001:Foo:pub}
      §C{DoWork} §/C
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var call = FirstCall(module);
        Assert.Equal("DoWork", call.Target);
        Assert.Empty(call.Arguments);
    }

    [Fact]
    public void StatementContext_MultiArg_WithAMarkers_ExplicitCloser_Parses()
    {
        // Multi-arg form: §C{Target} §A x §A y §/C
        var source = """
§M{m001:Test}
  §F{f001:Foo:pub}
      §C{Math.Max} §A INT:1 §A INT:2 §/C
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var call = FirstCall(module);
        Assert.Equal("Math.Max", call.Target);
        Assert.Equal(2, call.Arguments.Count);
    }

    [Fact]
    public void ExpressionContext_RequiresExplicitCloser_BaselineForRfc()
    {
        // Pins the contrast: in EXPRESSION context (here, as a binding
        // initializer) the call expression today REQUIRES §/C. The v0.6
        // call-elision RFC proposes lifting this requirement.
        //
        // This passes today because §/C is present:
        var source = """
§M{m001:Test}
  §F{f001:Foo:i32}
      §O{i32}
      §B{x} §C{Identity} §A INT:1 §/C
      §R x
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");
    }
}
