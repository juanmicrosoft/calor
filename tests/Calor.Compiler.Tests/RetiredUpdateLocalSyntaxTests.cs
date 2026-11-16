using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class RetiredUpdateLocalSyntaxTests
{

    [Fact]
    public void LocalFunctionIsRejectedWithLambdaBindingReplacement()
    {
        const string source = """
            module Demo Demo block
              func Run () → void
                func Local private () → void
                  !
            """;
        var diagnostics = new DiagnosticBag();
        new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("let binding") && d.Message.Contains("lambda"));
    }
}
