using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class RetiredUpdateLocalSyntaxTests
{
    [Theory]
    [InlineData("inc")]
    [InlineData("pre-inc")]
    [InlineData("dec")]
    [InlineData("pre-dec")]
    [InlineData("post-inc")]
    [InlineData("post-dec")]
    public void UpdateAliasesAreRejectedWithAssignmentReplacement(string op)
    {
        var diagnostics = new DiagnosticBag();
        new Parser(new Lexer($"§M{{Demo}}\n  §F{{Run:pub}} (i32:x) -> i32\n    §R ({op} x)\n",
            diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("§ASSIGN") && d.Message.Contains(op));
    }

    [Fact]
    public void LocalFunctionIsRejectedWithLambdaBindingReplacement()
    {
        const string source = """
            §M{Demo}
              §F{Run:pub} () -> void
                §F{Local:priv} () -> void
                  §E{}
            """;
        var diagnostics = new DiagnosticBag();
        new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("§B") && d.Message.Contains("§LAM"));
    }
}
