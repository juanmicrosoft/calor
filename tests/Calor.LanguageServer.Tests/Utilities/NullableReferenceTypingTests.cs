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
    [InlineData("input", "Foo")]
    [InlineData("input", "string")]
    public async Task LocalAndMemberReturnDiagnostic_UsesShapePolicyAndClearsOnSafeEditAsync(
        string value, string referenceType)
    {
        var source = $$"""
            module LocalMemberAnnotations LocalMemberAnnotations block
              class Foo
                field {{referenceType}}?:Value
                func Probe ({{referenceType}}?:input) → {{referenceType}}
                  !
                  let first = {{value}}
                  let second = first
                  return second
            """;
        var path = Path.Combine(Path.GetTempPath(), "local-member-" + Guid.NewGuid().ToString("N") + ".calr");
        using var document = new DocumentState(new Uri(path), source);
        await document.ReanalyzeAsync();
        var diagnostic = Assert.Single(document.Diagnostics.Where(d => d.Code == DiagnosticCode.NullableReturnFromNonNullable));
        Assert.True(BindingDiagnosticPolicy.IsCompilationError(diagnostic));
        var lsp = DiagnosticConverter.ToLspDiagnostic(diagnostic, source);
        Assert.Equal("calor", lsp.Source);
        Assert.Equal(PositionConverter.ToLspRange(diagnostic.Span, source), lsp.Range);
        Assert.Equal(referenceType == "string"
                ? BindingReceivingShape.ScalarString
                : BindingReceivingShape.Nominal,
            diagnostic.BindingContext?.Shape);
        Assert.Contains("source annotation: 'Annotated'", diagnostic.Message);
        var result = Compiler.Program.Compile(source, path);
        Assert.True(result.HasErrors);
        var compiler = Assert.Single(result.Diagnostics.Where(d => d.Code == diagnostic.Code));
        Assert.Equal(diagnostic.Span, compiler.Span);
        Assert.Equal(diagnostic.Severity, compiler.Severity);
        var update = await document.UpdateAsync(
            source.Replace($") → {referenceType}", $") → {referenceType}?", StringComparison.Ordinal), 1);
        Assert.True(update.Accepted);
        Assert.DoesNotContain(update.Snapshot.Diagnostics, d => d.Code == diagnostic.Code);
    }

    [Fact]
    public async Task NominalReturnDiagnostic_IsActiveAndClearsOnSafeEditAsync()
    {
        const string source = """
            module NominalIdentity NominalIdentity block
              class Foo
                func Get static (Foo?:input) → Foo?
                  !
                  return input

                func Probe static (Foo?:input) → Foo
                  !
                  return Foo.Get(input)
            """;
        var path = Path.Combine(Path.GetTempPath(), "nominal-" + Guid.NewGuid().ToString("N") + ".calr");
        using var document = new DocumentState(new Uri(path), source);
        await document.ReanalyzeAsync();
        var diagnostic = Assert.Single(document.Diagnostics.Where(d => d.Code == DiagnosticCode.NullableReturnFromNonNullable));
        Assert.True(BindingDiagnosticPolicy.IsCompilationError(diagnostic));
        var lsp = DiagnosticConverter.ToLspDiagnostic(diagnostic, source);
        Assert.Equal("calor", lsp.Source);
        Assert.Equal(PositionConverter.ToLspRange(diagnostic.Span, source), lsp.Range);
        Assert.Equal(BindingReceivingShape.Nominal, diagnostic.BindingContext?.Shape);
        var result = Compiler.Program.Compile(source, path);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, d => d.Code == diagnostic.Code);
        var update = await document.UpdateAsync(source.Replace(
            "Probe static (Foo?:input) → Foo", "Probe static (Foo?:input) → Foo?", StringComparison.Ordinal), 1);
        Assert.True(update.Accepted);
        Assert.DoesNotContain(update.Snapshot.Diagnostics, d => d.Code == diagnostic.Code);
    }

    [Theory]
    [InlineData("string?", "\"safe\"")]
    public async Task SupportedNullableReferences_HaveNoSpuriousTypingDiagnosticsAsync(string type, string value)
    {
        var source = $$"""
            module NullableTyping NullableTyping block
              func Probe ({{type}}:input) → {{type}}
                ! env
                let {{type}}:value = {{value}}
                return value
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
    [InlineData("§R §NN{str}", "?str")]
    public async Task RemovedConstructors_AreRejectedAndClearOnSafeEditAsync(string body, string returnType)
    {
        var source = $"§M{{NullableTyping}}\n  §F{{Probe:pub}} (?str:input) -> {returnType}\n    §E{{alloc}}\n    {body}\n";
        var path = Path.Combine(Path.GetTempPath(), "typing-" + Guid.NewGuid().ToString("N") + ".calr");
        using var document = new DocumentState(new Uri(path), source);
        await document.ReanalyzeAsync();
        var editor = document.Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error);
        var lsp = DiagnosticConverter.ToLspDiagnostic(editor, source);
        Assert.Equal("calor", lsp.Source);
        Assert.Equal(PositionConverter.ToLspRange(editor.Span, source), lsp.Range);
        foreach (var transpile in new[] { true, false })
        {
            var result = Compiler.Program.Compile(source, path, new CompilationOptions
            {
                EnableTypeChecking = false,
                UnsafeTranspileOnly = transpile
            });
            Assert.Contains(result.Diagnostics.Errors, compiler =>
                compiler.Code == editor.Code && compiler.Span == editor.Span
                && compiler.Severity == editor.Severity);
        }
        var update = await document.UpdateAsync(
            "module NullableTyping NullableTyping block\n  func Probe (string?:input) → string?\n    !\n    return input\n", 1);
        Assert.True(update.Accepted);
        Assert.DoesNotContain(update.Snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }
}
