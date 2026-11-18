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
    public static IEnumerable<object[]> StageBReceivingCases()
    {
        foreach (var (source, target, shape) in new[]
        {
            ("string?[]", "string[]", BindingReceivingShape.Array),
            ("List<string?>", "List<string>", BindingReceivingShape.Generic),
            ("Widget?", "Widget", BindingReceivingShape.Nominal)
        })
        foreach (var code in new[] { "Calor0272", "Calor0273", "Calor0274" })
            yield return [source, target, shape, code];
    }

    [Theory]
    [MemberData(nameof(StageBReceivingCases))]
    public async Task StageB_AllReceivingBoundariesMatchProductionAndClearAfterEditAsync(
        string sourceType, string targetType, BindingReceivingShape shape, string code)
    {
        var statement = code switch
        {
            "Calor0272" => $"let {targetType}:required = value",
            "Calor0273" => "return value",
            _ => "Take(value)"
        };
        var source = $$"""
            module StageBLsp
              class Widget
                func Id () → int
                  !
                  return 1
              func Take ({{targetType}}:value) → void
                !
              func Probe ({{sourceType}}:value) → {{(code == "Calor0273" ? targetType : "void")}}
                !
                {{statement}}
            """;
        var path = Path.Combine(Path.GetTempPath(), $"stage-b-lsp-{Guid.NewGuid():N}.calr");
        using var document = new DocumentState(new Uri(path), source);
        await document.ReanalyzeAsync();
        AssertNativeSyntax(document);
        var diagnostic = Assert.Single(document.Diagnostics.Where(d => d.Code == code));
        Assert.Equal(code switch
        {
            "Calor0272" => BindingReceivingBoundary.Initializer,
            "Calor0273" => BindingReceivingBoundary.NativeReturn,
            _ => BindingReceivingBoundary.MethodArgument
        }, diagnostic.BindingContext?.Boundary);
        Assert.Equal(shape, diagnostic.BindingContext?.Shape);
        Assert.Equal("value", source.Substring(diagnostic.Span.Start, diagnostic.Span.Length));
        var lsp = DiagnosticConverter.ToLspDiagnostic(diagnostic, source);
        Assert.Equal("calor", lsp.Source);
        Assert.Equal(OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Error, lsp.Severity);
        Assert.Equal(PositionConverter.ToLspRange(diagnostic.Span, source), lsp.Range);
        var result = Compiler.Program.Compile(source, path);
        var propagated = Assert.Single(result.Diagnostics.Where(d => d.Code == code));
        Assert.Equal(diagnostic.Span, propagated.Span);
        Assert.Equal(diagnostic.Severity, propagated.Severity);
        var safe = source.Replace($"({sourceType}:value)", $"({targetType}:value)", StringComparison.Ordinal);
        Assert.NotEqual(source, safe);
        var update = await document.UpdateAsync(safe, 1);
        Assert.True(update.Accepted);
        Assert.DoesNotContain(update.Snapshot.Diagnostics, d => d.Code == code);
        var safeResult = Compiler.Program.Compile(safe, path);
        Assert.False(safeResult.HasErrors, string.Join(Environment.NewLine, safeResult.Diagnostics));
    }

    [Theory]
    [InlineData("string[]?", "container")]
    [InlineData("string?[]", "elements")]
    [InlineData("string?[]?", "container and string elements")]
    public async Task StageB_ArrayInputs_UseActiveArrayContextAndExactSpanAsync(string sourceType, string component)
    {
        var source = $$"""
            module ArrayRouting
              func Take (string[]:items) → int
                !
                return 1
              func Caller ({{sourceType}}:values) → int
                !
                let first = values
                let second = first
                return Take(items:second)
            """;
        var path = Path.Combine(Environment.CurrentDirectory, "array-routing-" + Guid.NewGuid().ToString("N") + ".calr");
        using var document = new DocumentState(new Uri(path), source);
        await document.ReanalyzeAsync();
        AssertNativeSyntax(document);
        var diagnostic = Assert.Single(document.Diagnostics.Where(d => d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter));
        Assert.Equal(BindingReceivingBoundary.MethodArgument, diagnostic.BindingContext?.Boundary);
        Assert.Equal(BindingReceivingShape.Array, diagnostic.BindingContext?.Shape);
        Assert.Equal("second", source.Substring(diagnostic.Span.Start, diagnostic.Span.Length));
        Assert.Contains(component, diagnostic.Message);
        var lsp = DiagnosticConverter.ToLspDiagnostic(diagnostic, source);
        Assert.Equal("calor", lsp.Source);
        Assert.Equal(PositionConverter.ToLspRange(diagnostic.Span, source), lsp.Range);
        Assert.Equal(OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Error, lsp.Severity);
        var compiled = Compiler.Program.Compile(source, path);
        Assert.True(compiled.HasErrors);
        Assert.Contains(compiled.Diagnostics, d => d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ScalarStringActivation_DoesNotDependOnNativeOverloadReplacementAsync(
        bool expression, bool objectOverload)
    {
        var extra = objectOverload
            ? "  func Take (object:value) → int\n    !\n    return 2\n"
            : "";
        var source = "module Routing\n  func Take (string:value) → int\n    !\n    return 1\n"
            + extra
            + "  func Caller (string?:value) → int\n    !\n    "
            + (expression ? "return " : "") + "Take(value)\n"
            + (expression ? "" : "    return 0\n");
        var path = Path.Combine(Path.GetTempPath(), "native-routing-" + Guid.NewGuid().ToString("N") + ".calr");
        using var document = new DocumentState(new Uri(path), source);
        await document.ReanalyzeAsync();
        AssertNativeSyntax(document);
        var diagnostic = Assert.Single(document.Diagnostics.Where(d =>
            d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter));
        Assert.Equal(!objectOverload, diagnostic.BindingContext!.ReplacesNativeOverloadError);
        var lsp = DiagnosticConverter.ToLspDiagnostic(diagnostic, source);
        Assert.Equal("calor", lsp.Source);
        Assert.Equal(PositionConverter.ToLspRange(diagnostic.Span, source), lsp.Range);
        var compiled = Compiler.Program.Compile(source, path);
        Assert.True(compiled.HasErrors);
        var compiler = Assert.Single(compiled.Diagnostics.Where(d => d.Code == diagnostic.Code));
        Assert.Equal(diagnostic.Span, compiler.Span);
        Assert.Equal(diagnostic.Severity, compiler.Severity);

        var safe = source.Replace("(string:value)", "(string?:value)", StringComparison.Ordinal);
        Assert.NotEqual(source, safe);
        var update = await document.UpdateAsync(safe, 1);
        Assert.True(update.Accepted);
        Assert.DoesNotContain(update.Snapshot.Diagnostics, d =>
            d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
    }

    [Fact]
    public async Task CodeActions_PreserveAnalysisOnlyProvenanceAsync()
    {
        const string source = """
            module Routing
              func Helper () → int
                !
                return 42
              func Main () → int
                !
                let x = Helper
                return x
            """;
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From(
            new Uri(Path.Combine(Path.GetTempPath(), "routing-action-" + Guid.NewGuid().ToString("N") + ".calr")));
        var workspace = new WorkspaceState();
        var document = workspace.GetOrCreate(uri, source);
        try
        {
            await document.ReanalyzeAsync();
            AssertNativeSyntax(document);
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
    [InlineData("let string:x = System.Environment.GetEnvironmentVariable(\"ROUTING_UNSET\")", "void", "Calor0272")]
    [InlineData("return System.Environment.GetEnvironmentVariable(\"ROUTING_UNSET\")", "string", "Calor0273")]
    [InlineData("return System.Int32.Parse(System.Environment.GetEnvironmentVariable(\"ROUTING_UNSET\"))", "int", "Calor0274")]
    public async Task ActiveScalarFindings_AreLabeledWithoutChangingBinderSeverityOrSpanAsync(
        string body, string returnType, string code)
    {
        var source = $"module Routing\n  func Probe () → {returnType}\n    ! env\n    {body}\n";
        var path = Path.Combine(Path.GetTempPath(), "routing-" + Guid.NewGuid().ToString("N") + ".calr");
        using var document = new DocumentState(new Uri(path), source);
        await document.ReanalyzeAsync();
        AssertNativeSyntax(document);
        var findings = document.Diagnostics.Where(d => d.Code == code).ToArray();
        Assert.True(findings.Length == 1, string.Join(Environment.NewLine, document.Diagnostics));
        var diagnostic = Assert.Single(findings);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(BindingReceivingShape.ScalarString, diagnostic.BindingContext?.Shape);
        var lsp = DiagnosticConverter.ToLspDiagnostic(diagnostic, source);
        Assert.Equal("calor", lsp.Source);
        Assert.Equal(code, lsp.Code);
        Assert.Equal(OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity.Error, lsp.Severity);
        Assert.Equal(PositionConverter.ToLspRange(diagnostic.Span, source), lsp.Range);
        Assert.Equal("calor", DiagnosticConverter.ToLspDiagnosticSingleLine(diagnostic).Source);

        var compiled = Compiler.Program.Compile(source, path);
        Assert.True(compiled.HasErrors);
        var compiler = Assert.Single(compiled.Diagnostics.Where(d => d.Code == code));
        Assert.Equal(diagnostic.Span, compiler.Span);
        Assert.Equal(diagnostic.Severity, compiler.Severity);
        var update = await document.UpdateAsync("module Routing\n  func Probe () → string\n    !\n    return \"safe\"\n", 1);
        Assert.True(update.Accepted);
        Assert.DoesNotContain(update.Snapshot.Diagnostics, d => d.Code == code);
    }

    [Fact]
    public async Task ActiveBinderError_AgreesWithApiAcrossOptOutModesAsync()
    {
        const string source = """
            module Routing
              func Probe () → int
                !
                return 1
              func Probe () → int
                !
                return 2
            """;
        var path = Path.Combine(Path.GetTempPath(), "routing-" + Guid.NewGuid().ToString("N") + ".calr");
        using var document = new DocumentState(new Uri(path), source);
        await document.ReanalyzeAsync();
        AssertNativeSyntax(document);
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

    private static void AssertNativeSyntax(DocumentState document)
    {
        Assert.StartsWith("module ", document.Source);
        Assert.NotNull(document.Ast);
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.IsError
            && (diagnostic.Code.StartsWith("Calor00", StringComparison.Ordinal)
                || diagnostic.Code.StartsWith("Calor01", StringComparison.Ordinal)
                || diagnostic.Code.StartsWith("Calor083", StringComparison.Ordinal)));
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
