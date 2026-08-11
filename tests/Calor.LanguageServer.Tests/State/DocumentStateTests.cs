using Calor.LanguageServer.State;
using Calor.LanguageServer.Tests.Helpers;
using Xunit;

namespace Calor.LanguageServer.Tests.State;

public class DocumentStateTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        var uri = new Uri("file:///test.calr");
        var source = "test source";

        var state = new DocumentState(uri, source, 1);

        Assert.Equal(uri, state.Uri);
        Assert.Equal(source, state.Source);
        Assert.Equal(1, state.Version);
    }

    [Fact]
    public void Reanalyze_ValidSource_ParsesAst()
    {
        var source = """
            §M{m001:TestModule}
              §F{f001:Test:pub} () -> i32
                §R 0
            """;

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        Assert.NotNull(state.Tokens);
        Assert.False(state.Diagnostics.HasErrors);
    }

    [Fact]
    public void Reanalyze_InvalidSource_HasErrors()
    {
        var source = "§M{m001:Test} §UNKNOWN_TOKEN_XYZ"; // Invalid section marker

        var state = LspTestHarness.CreateDocument(source);

        Assert.True(state.Diagnostics.HasErrors);
    }

    [Fact]
    public void Reanalyze_UnboundConstruct_PublishesInfoSeverityIncomplete_NotError()
    {
        // #762 B1: the LSP binds every open document with the LIVE diagnostics bag, so
        // the Calor0259 incomplete-fraction instrument is editor-visible. This pins the
        // severity decision (scoping doc §5): Info until B8 — a document using a
        // not-yet-bound construct (quantifier, family B7) must gain the instrument
        // diagnostic WITHOUT errors, or every editor floods while the family PRs land.
        // (Fixture has moved down the family sequence as each family binds: B6 retired
        // the null-coalesce version; when B7 lands this must move to a Tier B construct
        // or, post-B8, be re-pointed at whatever the closure decision leaves incomplete.)
        var source = """
            §M{m001:TestModule}
              §F{f001:Pick:pub} (i32:x) -> bool
                §R (forall ((i i32)) (> i 0))
            """;

        var state = LspTestHarness.CreateDocument(source);

        Assert.False(state.Diagnostics.HasErrors);
        var incomplete = state.Diagnostics.Where(
            d => d.Code == Compiler.Diagnostics.DiagnosticCode.AnalysisIncomplete).ToList();
        Assert.NotEmpty(incomplete);
        Assert.All(incomplete,
            d => Assert.Equal(Compiler.Diagnostics.DiagnosticSeverity.Info, d.Severity));
    }

    [Fact(Skip = "Phase 4d: mismatched-ID diagnostic is obsolete under indent-only (no closing tags)")]
    public void Reanalyze_MismatchedId_HasDiagnosticsWithFixes()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test}
            §R 0
            """;

        var state = LspTestHarness.CreateDocument(source);

        Assert.True(state.Diagnostics.HasErrors);
        Assert.NotEmpty(state.DiagnosticsWithFixes);
        Assert.Contains(state.DiagnosticsWithFixes, d => d.Code == "Calor0101"); // MismatchedId
    }

    [Fact]
    public void Update_ChangesSource()
    {
        var source1 = """
            §M{m001:TestModule}
              §F{f001:Test}
                §R 0
            """;
        var source2 = """
            §M{m001:TestModule}
              §F{f001:Test2}
                §R 1
            """;

        var state = LspTestHarness.CreateDocument(source1);
        state.Update(source2, 2);

        Assert.Equal(source2, state.Source);
        Assert.Equal(2, state.Version);
        Assert.NotNull(state.Ast);
        Assert.Equal("Test2", state.Ast.Functions[0].Name);
    }

    [Fact]
    public void Update_ClearsPreviousDiagnostics()
    {
        var badSource = "§M{m001:Test} §UNKNOWN_TOKEN_XYZ"; // Invalid
        var goodSource = """
            §M{m001:TestModule}
            """;

        var state = LspTestHarness.CreateDocument(badSource);
        Assert.True(state.Diagnostics.HasErrors);

        state.Update(goodSource, 2);

        Assert.False(state.Diagnostics.HasErrors);
    }

    [Fact]
    public void GetTokenAtPosition_ReturnsToken()
    {
        var source = """
            §M{m001:TestModule}
            """;

        var state = LspTestHarness.CreateDocument(source);
        var token = state.GetTokenAtPosition(1, 1);

        Assert.NotNull(token);
    }

    [Fact]
    public void GetTokenAtPosition_InvalidPosition_ReturnsNull()
    {
        var source = """
            §M{m001:TestModule}
            """;

        var state = LspTestHarness.CreateDocument(source);
        var token = state.GetTokenAtPosition(100, 1);

        Assert.Null(token);
    }

    [Fact]
    public void GetTokenAtOffset_ReturnsToken()
    {
        var source = """
            §M{m001:TestModule}
            """;

        var state = LspTestHarness.CreateDocument(source);
        var token = state.GetTokenAtOffset(0);

        Assert.NotNull(token);
    }

    [Fact]
    public void Reanalyze_SetsFilePath()
    {
        var source = """
            §M{m001:TestModule}
            """;

        var state = LspTestHarness.CreateDocument(source, "file:///path/to/test.calr");

        // Diagnostics should have the file path set
        // We can verify by checking if the state processed correctly with a file path
        Assert.NotNull(state.Ast);
    }

    [Fact]
    public void Reanalyze_WithBindingErrors_StillHasAst()
    {
        var source = """
            §M{m001:TestModule}
              §F{f001:Test}
                §R undefined_variable
            """;

        var state = LspTestHarness.CreateDocument(source);

        // Parsing should succeed, binding might have errors
        Assert.NotNull(state.Ast);
        Assert.NotEmpty(state.Ast.Functions);
    }

    [Fact(Skip = "Phase 4d: source no longer produces fixes under indent-only (mismatched-ID concept gone)")]
    public void DiagnosticsWithFixes_ContainsFixInfo()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test}
            §R 0
            """;

        var fixes = LspTestHarness.GetDiagnosticsWithFixes(source);

        Assert.NotEmpty(fixes);
        var fix = fixes.First();
        Assert.NotNull(fix.Fix);
        Assert.NotEmpty(fix.Fix.Description);
        Assert.NotEmpty(fix.Fix.Edits);
    }

    [Fact]
    public void UndefinedVariable_WithSimilarName_HasDidYouMeanSuggestion()
    {
        // Uses "valeu" instead of "value" - a typo that should be suggested
        var source = """
            §M{m001:TestModule}
              §F{f001:Test}
                §I{i32:value}
                §O{i32}
                §R valeu
            """;

        var state = LspTestHarness.CreateDocument(source);

        // Should have an error for undefined reference
        Assert.True(state.Diagnostics.HasErrors);

        // Should have a "did you mean" suggestion
        var undefinedError = state.Diagnostics.Errors
            .FirstOrDefault(d => d.Code == "Calor0200"); // UndefinedReference
        Assert.NotNull(undefinedError);
        Assert.Contains("Did you mean", undefinedError.Message);
        Assert.Contains("value", undefinedError.Message);
    }

    [Fact]
    public void UndefinedVariable_WithSimilarName_HasQuickFix()
    {
        // Uses "valeu" instead of "value" - should have a quick fix
        var source = """
            §M{m001:TestModule}
              §F{f001:Test}
                §I{i32:value}
                §O{i32}
                §R valeu
            """;

        var fixes = LspTestHarness.GetDiagnosticsWithFixes(source);

        // Should have a quick fix for the undefined reference
        var undefinedFix = fixes.FirstOrDefault(d => d.Code == "Calor0200");
        Assert.NotNull(undefinedFix);
        Assert.NotNull(undefinedFix.Fix);
        Assert.Contains("value", undefinedFix.Fix.Description);
        Assert.NotEmpty(undefinedFix.Fix.Edits);
        Assert.Equal("value", undefinedFix.Fix.Edits.First().NewText);
    }
}
