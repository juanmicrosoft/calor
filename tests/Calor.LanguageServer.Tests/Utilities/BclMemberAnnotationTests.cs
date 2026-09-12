using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.LanguageServer.State;
using Calor.LanguageServer.Utilities;
using Xunit;

namespace Calor.LanguageServer.Tests.Utilities;

public class BclMemberAnnotationTests
{
    [Theory]
    [InlineData("§B{value:str} System.Environment.ProcessPath", "void", DiagnosticCode.NullableToNonNullableBinding)]
    [InlineData("§R System.Environment.ProcessPath", "str", DiagnosticCode.NullableReturnFromNonNullable)]
    [InlineData("§R §C{System.Int32.Parse} §A System.Environment.ProcessPath §/C", "i32", DiagnosticCode.NullableArgumentToNonNullableParameter)]
    public async Task ScalarMemberDiagnostic_IsAnalysisOnlyAndClearsOnSafeEditAsync(
        string body, string returnType, string code)
    {
        var source = $"§M{{m1:Members}}\n  §F{{f1:Probe:pub}} () -> {returnType}\n    {body}\n";
        var path = Path.Combine(Path.GetTempPath(), "members-" + Guid.NewGuid().ToString("N") + ".calr");
        using var document = new DocumentState(new Uri(path), source);
        await document.ReanalyzeAsync();
        var diagnostic = Assert.Single(document.Diagnostics.Where(d => d.Code == code));
        Assert.Equal(BindingReceivingShape.ScalarString, diagnostic.BindingContext?.Shape);
        Assert.False(BindingDiagnosticPolicy.IsCompilationError(diagnostic));
        var lsp = DiagnosticConverter.ToLspDiagnostic(diagnostic, source);
        Assert.Equal("calor (analysis only)", lsp.Source);
        Assert.Equal(PositionConverter.ToLspRange(diagnostic.Span, source), lsp.Range);
        Assert.Equal("System.Environment.ProcessPath", source.Substring(diagnostic.Span.Start, diagnostic.Span.Length));
        var updated = await document.UpdateAsync(source.Replace("ProcessPath", "CurrentDirectory", StringComparison.Ordinal), 1);
        Assert.True(updated.Accepted);
        Assert.DoesNotContain(updated.Snapshot.Diagnostics, d => d.Code == code);
    }
}
