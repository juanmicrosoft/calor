using Calor.Compiler;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.LanguageServer.State;
using Calor.LanguageServer.Utilities;
using Xunit;

namespace Calor.LanguageServer.Tests.Utilities;

public class NullableReferenceTypingTests
{
    [Theory]
    [InlineData("?str", "\"safe\"")]
    [InlineData("?string", "§C{System.Environment.GetEnvironmentVariable} §A \"CALOR_T1_EDITOR\" §/C")]
    public async Task SupportedNullableReferences_HaveNoSpuriousTypingDiagnosticsAsync(string type, string value)
    {
        var source = $$"""
            §M{m1:NullableTyping}
              §F{f1:Probe:pub} ({{type}}:input) -> {{type}}
                §E{env}
                §B{value:{{type}}} {{value}}
                §R value
            """;
        var path = Path.Combine(Path.GetTempPath(), "typing-" + Guid.NewGuid().ToString("N") + ".calr");
        using var document = new DocumentState(new Uri(path), source);
        await document.ReanalyzeAsync();
        Assert.DoesNotContain(document.Diagnostics,
            d => d.Code is DiagnosticCode.UndefinedReference or DiagnosticCode.TypeMismatch
                or DiagnosticCode.ReferenceOptionMismatch or DiagnosticCode.NullableToNonNullableBinding);
        foreach (var checking in new[] { true, false })
        {
            var result = Compiler.Program.Compile(source, path,
                new CompilationOptions { EnableTypeChecking = checking });
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.UndefinedReference);
        }
    }

    [Theory]
    [InlineData("§B{x:?str} §SM \"value\"", "void")]
    [InlineData("§R §SM \"value\"", "?str")]
    [InlineData("§B{x:Option<str>} input", "void")]
    public async Task RepresentationMismatch_IsActiveAndClearsOnSafeEditAsync(string body, string returnType)
    {
        var source = $"§M{{m1:NullableTyping}}\n  §F{{f1:Probe:pub}} (?str:input) -> {returnType}\n    §E{{alloc}}\n    {body}\n";
        var path = Path.Combine(Path.GetTempPath(), "typing-" + Guid.NewGuid().ToString("N") + ".calr");
        using var document = new DocumentState(new Uri(path), source);
        await document.ReanalyzeAsync();
        var editor = Assert.Single(document.Diagnostics.Where(d => d.Code == DiagnosticCode.ReferenceOptionMismatch));
        var lsp = DiagnosticConverter.ToLspDiagnostic(editor, source);
        Assert.Equal("calor", lsp.Source);
        Assert.Equal(PositionConverter.ToLspRange(editor.Span, source), lsp.Range);
        Assert.True(BindingDiagnosticPolicy.IsCompilationError(editor));
        foreach (var transpile in new[] { true, false })
        {
            var result = Compiler.Program.Compile(source, path, new CompilationOptions
            {
                EnableTypeChecking = false,
                UnsafeTranspileOnly = transpile
            });
            var compiler = Assert.Single(result.Diagnostics.Errors);
            Assert.Equal(editor.Code, compiler.Code);
            Assert.Equal(editor.Span, compiler.Span);
            Assert.Equal(editor.Severity, compiler.Severity);
        }
        var update = await document.UpdateAsync(
            "§M{m1:NullableTyping}\n  §F{f1:Probe:pub} (?str:input) -> ?str\n    §R input\n", 1);
        Assert.True(update.Accepted);
        Assert.DoesNotContain(update.Snapshot.Diagnostics, d => d.Code == DiagnosticCode.ReferenceOptionMismatch);
    }
}
