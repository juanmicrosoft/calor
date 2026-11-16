using Calor.LanguageServer.Tests.Helpers;
using Xunit;

namespace Calor.LanguageServer.Tests.Handlers;

public class CodeActionHandlerTests
{
    [Fact]
    public void ValidSource_NoFixes()
    {
        var source = """
            module TestModule TestModule block
              func Test () → int
                !
                return 0
            """;

        var fixes = LspTestHarness.GetDiagnosticsWithFixes(source);

        Assert.Empty(fixes);
    }

    [Fact]
    public void UndefinedReference_WithSimilarName_GeneratesFix()
    {
        var source = """
            module TestModule TestModule block
              func Test () → int
                !
                let counter = 0
                return couner
            """;

        var fixes = LspTestHarness.GetDiagnosticsWithFixes(source);

        Assert.NotEmpty(fixes);
        var undefinedFix = fixes.FirstOrDefault(f => f.Code == "Calor0200");
        Assert.NotNull(undefinedFix);
        Assert.Contains("counter", undefinedFix.Fix.Description);
        Assert.Single(undefinedFix.Fix.Edits);
        Assert.Equal("counter", undefinedFix.Fix.Edits[0].NewText);
    }

    [Fact]
    public void UndefinedReference_NoSimilarName_NoFix()
    {
        var source = """
            module TestModule TestModule block
              func Test () → int
                !
                let counter = 0
                return xyz
            """;

        var fixes = LspTestHarness.GetDiagnosticsWithFixes(source);

        // Should have no fix for 'xyz' because it's not similar to 'counter'
        var undefinedFix = fixes.FirstOrDefault(f => f.Code == "Calor0200");
        Assert.Null(undefinedFix);

        // But should still have the error diagnostic
        var diagnostics = LspTestHarness.GetDiagnostics(source);
        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Code == "Calor0200" && d.Message.Contains("xyz"));
    }

    [Fact]
    public void UndefinedReference_SimilarParameter_GeneratesFix()
    {
        var source = """
            module TestModule TestModule block
              func Add (int:value) → int
                !
                return valeu
            """;

        var fixes = LspTestHarness.GetDiagnosticsWithFixes(source);

        Assert.NotEmpty(fixes);
        var undefinedFix = fixes.FirstOrDefault(f => f.Code == "Calor0200");
        Assert.NotNull(undefinedFix);
        Assert.Contains("value", undefinedFix.Fix.Description);
        Assert.Equal("value", undefinedFix.Fix.Edits[0].NewText);
    }

    [Fact]
    public void FunctionUsedAsVariable_GeneratesFix()
    {
        var source = """
            module TestModule TestModule block
              func Helper () → int
                !
                return 42

              func Main () → int
                !
                let x = Helper
                return x
            """;

        var fixes = LspTestHarness.GetDiagnosticsWithFixes(source);

        Assert.NotEmpty(fixes);
        var typeMismatchFix = fixes.FirstOrDefault(f => f.Code == "Calor0202");
        Assert.NotNull(typeMismatchFix);
        Assert.Contains("Call", typeMismatchFix.Fix.Description);
        Assert.Contains("Helper", typeMismatchFix.Fix.Description);
    }

    [Fact]
    public void DuplicateParameter_GeneratesFix()
    {
        var source = """
            module TestModule TestModule block
              func Add (int:x, int:x) → int
                !
                return x
            """;

        var fixes = LspTestHarness.GetDiagnosticsWithFixes(source);

        Assert.NotEmpty(fixes);
        var duplicateFix = fixes.FirstOrDefault(f => f.Code == "Calor0201");
        Assert.NotNull(duplicateFix);
        Assert.Contains("Rename", duplicateFix.Fix.Description);
        Assert.Contains("x2", duplicateFix.Fix.Description);
    }

    [Fact]
    public void MultipleFixes_AllHaveCorrectEdits()
    {
        var source = """
            module TestModule TestModule block
              func Calc () → int
                !
                let counter = 0
                let result = couner
                return reslt
            """;

        var fixes = LspTestHarness.GetDiagnosticsWithFixes(source);

        // Should have two fixes: couner -> counter, reslt -> result
        Assert.True(fixes.Count >= 2);

        var counterFix = fixes.FirstOrDefault(f => f.Fix.Edits[0].NewText == "counter");
        var resultFix = fixes.FirstOrDefault(f => f.Fix.Edits[0].NewText == "result");

        Assert.NotNull(counterFix);
        Assert.NotNull(resultFix);
    }

    [Fact]
    public void AppliedFix_ProducesValidSource()
    {
        // Test that applying a typo fix results in valid code
        var source = """
            module TestModule TestModule block
              func Test () → int
                !
                let counter = 0
                return couner
            """;

        var fixes = LspTestHarness.GetDiagnosticsWithFixes(source);
        var fix = fixes.First(f => f.Code == "Calor0200");

        // Apply the fix manually
        var fixedSource = source.Replace("couner", fix.Fix.Edits[0].NewText);

        // Verify the fixed source has no undefined reference errors
        var diagnostics = LspTestHarness.GetDiagnostics(fixedSource);
        Assert.DoesNotContain(diagnostics, d => d.Code == "Calor0200");
    }
    #region Operator Typo Suggestion Tests

    [Fact]
    public void NameofOperator_CompilesWithNoErrors()
    {
        var source = """
            module TestModule TestModule block
              func Test () → string
                !
                return nameof(x)
            """;

        var diagnostics = LspTestHarness.GetDiagnostics(source);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
    }

    #endregion
}
