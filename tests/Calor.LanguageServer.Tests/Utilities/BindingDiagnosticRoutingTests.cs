using Calor.Compiler;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Parsing;
using Calor.LanguageServer.State;
using Calor.LanguageServer.Utilities;
using Xunit;

namespace Calor.LanguageServer.Tests.Utilities;

public class BindingDiagnosticRoutingTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NativeStringOwnership_IsNotInferredFromDiagnosticCodeOrCachedSourceAsync(
        bool expression, bool objectOverload)
    {
        var extra = objectOverload
            ? "  §F{object:Take:pub} (object:value) -> i32\n    §E{}\n    §R 2\n"
            : "";
        var source = "§M{m1:Routing}\n  §F{take:Take:pub} (str:value) -> i32\n    §E{}\n    §R 1\n"
            + extra
            + "  §F{caller:Caller:pub} (?str:value) -> i32\n    §E{}\n    "
            + (expression ? "§R " : "") + "§C{Take} §A value §/C\n"
            + (expression ? "" : "    §R 0\n");
        var path = Path.Combine(Path.GetTempPath(), "native-routing-" + Guid.NewGuid().ToString("N") + ".calr");
        using var document = new DocumentState(new Uri(path), source);
        await document.ReanalyzeAsync();
        var diagnostic = Assert.Single(document.Diagnostics.Where(d =>
            d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter));
        Assert.Equal(!objectOverload, diagnostic.BindingContext!.ReplacesNativeOverloadError);
        var lsp = DiagnosticConverter.ToLspDiagnostic(diagnostic, source);
        Assert.Equal(objectOverload ? "calor (analysis only)" : "calor", lsp.Source);
        Assert.Equal(PositionConverter.ToLspRange(diagnostic.Span, source), lsp.Range);
        Assert.Equal(!objectOverload, Compiler.Program.Compile(source, path).HasErrors);

        var safe = source.Replace("(str:value)", "(?str:value)", StringComparison.Ordinal);
        var update = await document.UpdateAsync(safe, 1);
        Assert.True(update.Accepted);
        Assert.DoesNotContain(update.Snapshot.Diagnostics, d =>
            d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
    }

    [Fact]
    public async Task CodeActions_PreserveAnalysisOnlyProvenanceAsync()
    {
        const string source = """
            §M{m1:Routing}
              §F{f1:Helper:pub} () -> i32
                §R 42
              §F{f2:Main:pub} () -> i32
                §B{x} Helper
                §R x
            """;
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From(
            new Uri(Path.Combine(Path.GetTempPath(), "routing-action-" + Guid.NewGuid().ToString("N") + ".calr")));
        var workspace = new WorkspaceState();
        var document = workspace.GetOrCreate(uri, source);
        try
        {
            await document.ReanalyzeAsync();
            var handler = new LanguageServer.Handlers.CodeActionHandler(workspace);
            var actions = await handler.Handle(new OmniSharp.Extensions.LanguageServer.Protocol.Models.CodeActionParams
            {
                TextDocument = new OmniSharp.Extensions.LanguageServer.Protocol.Models.TextDocumentIdentifier(uri),
                Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(0, 0, 20, 0),
                Context = new OmniSharp.Extensions.LanguageServer.Protocol.Models.CodeActionContext
                {
                    Diagnostics = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Container<
                        OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>()
                }
            }, CancellationToken.None);
            Assert.NotNull(actions);
            var diagnostic = Assert.Single(actions.SelectMany(action =>
                action.CodeAction?.Diagnostics ?? []).Where(d => d.Code == DiagnosticCode.TypeMismatch));
            Assert.Equal("calor (analysis only)", diagnostic.Source);
            Assert.Equal(OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.NotNull(diagnostic.Data);
        }
        finally
        {
            workspace.Remove(uri);
        }
    }

    [Theory]
    [InlineData("§B{x:str} §C{System.Environment.GetEnvironmentVariable} §A \"ROUTING_UNSET\" §/C", "void", "Calor0272")]
    [InlineData("§R §C{System.Environment.GetEnvironmentVariable} §A \"ROUTING_UNSET\" §/C", "str", "Calor0273")]
    [InlineData("§R §C{System.Int32.Parse} §A §C{System.Environment.GetEnvironmentVariable} §A \"ROUTING_UNSET\" §/C §/C", "i32", "Calor0274")]
    public async Task AnalysisOnlyFindings_AreLabeledWithoutChangingBinderSeverityOrSpanAsync(
        string body, string returnType, string code)
    {
        var source = $"§M{{m1:Routing}}\n  §F{{f1:Probe:pub}} () -> {returnType}\n    §E{{env}}\n    {body}\n";
        var path = Path.Combine(Path.GetTempPath(), "routing-" + Guid.NewGuid().ToString("N") + ".calr");
        using var document = new DocumentState(new Uri(path), source);
        await document.ReanalyzeAsync();
        var diagnostic = Assert.Single(document.Diagnostics.Where(d => d.Code == code));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(BindingReceivingShape.ScalarString, diagnostic.BindingContext?.Shape);
        var lsp = DiagnosticConverter.ToLspDiagnostic(diagnostic, source);
        Assert.Equal("calor (analysis only)", lsp.Source);
        Assert.Equal(code, lsp.Code);
        Assert.Equal(OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Error, lsp.Severity);
        Assert.Equal(PositionConverter.ToLspRange(diagnostic.Span, source), lsp.Range);
        Assert.Equal("calor (analysis only)", DiagnosticConverter.ToLspDiagnosticSingleLine(diagnostic).Source);

        var compiled = Compiler.Program.Compile(source, path);
        Assert.False(compiled.HasErrors);
        Assert.DoesNotContain(compiled.Diagnostics, d => d.Code == code);
        var update = await document.UpdateAsync("§M{m1:Routing}\n  §F{f1:Probe:pub} () -> str\n    §R \"safe\"\n", 1);
        Assert.True(update.Accepted);
        Assert.DoesNotContain(update.Snapshot.Diagnostics, d => d.Code == code);
    }

    [Fact]
    public async Task ActiveBinderError_AgreesWithApiAcrossOptOutModesAsync()
    {
        const string source = """
            §M{m1:Routing}
              §F{f1:Probe:pub} () -> i32
                §R 1
              §F{f2:Probe:pub} () -> i32
                §R 2
            """;
        var path = Path.Combine(Path.GetTempPath(), "routing-" + Guid.NewGuid().ToString("N") + ".calr");
        using var document = new DocumentState(new Uri(path), source);
        await document.ReanalyzeAsync();
        var editor = Assert.Single(document.Diagnostics.Where(d => d.Code == DiagnosticCode.DuplicateFunctionSignature));
        var lsp = DiagnosticConverter.ToLspDiagnostic(editor, source);
        Assert.Equal("calor", lsp.Source);
        foreach (var typeCheck in new[] { true, false })
        foreach (var transpile in new[] { true, false })
        foreach (var effects in new[] { true, false })
        {
            var result = Compiler.Program.Compile(source, path, new CompilationOptions
            {
                EnableTypeChecking = typeCheck,
                UnsafeTranspileOnly = transpile,
                EnforceEffects = effects,
                UnknownCallPolicy = UnknownCallPolicy.Permissive
            });
            Assert.True(result.HasErrors);
            var compiler = Assert.Single(result.Diagnostics.Where(d => d.Code == editor.Code));
            Assert.Equal(editor.Span, compiler.Span);
            Assert.Equal(editor.Severity, compiler.Severity);
            // Declaration identities use URI vs file-path provenance; code/span/severity are the parity contract.
        }
    }

    [Fact]
    public void SameCodeFromAnotherPass_IsNotRelabeledAsBinderAnalysis()
    {
        var diagnostic = new Diagnostic(DiagnosticCode.TypeMismatch, "Type checker error", new TextSpan(0, 1, 1, 1));
        Assert.Equal("calor", DiagnosticConverter.ToLspDiagnostic(diagnostic, "x").Source);
        var fix = new DiagnosticWithFix(DiagnosticCode.TypeMismatch, "Binder analysis", diagnostic.Span,
            new SuggestedFix("Use a call", new TextEdit("x.calr", 1, 1, 1, 2, "x")))
        { BindingContext = BindingDiagnosticContext.General };
        var converted = DiagnosticConverter.ToLspDiagnostic(fix, "x");
        Assert.Equal("calor (analysis only)", converted.Source);
        Assert.Equal("Use a call", converted.Data?.ToString());
    }
}
