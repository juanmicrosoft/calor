using Calor.LanguageServer.Tests.Helpers;
using Xunit;

namespace Calor.LanguageServer.Tests.Handlers;

public class CodeActionHandlerTests
{
    [Fact]
    public void ValidSource_NoFixes()
    {
        var source = """
            §M{m001:TestModule}
              §F{f001:Test:pub} () -> i32
                §R 0
            """;

        var fixes = LspTestHarness.GetDiagnosticsWithFixes(source);

        Assert.Empty(fixes);
    }

    [Fact]
    public void FixApplied_SourceIsValid()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test:pub} () -> i32
            §R 0
            """;

        // The fix should change f002 to f001
        var fixedSource = source.Replace("§/F{f002}", "§/F{f001}");

        var diagnostics = LspTestHarness.GetDiagnostics(fixedSource);

        Assert.False(diagnostics.HasErrors);
    }

    [Fact]
    public void UndefinedReference_WithSimilarName_GeneratesFix()
    {
        var source = """
            §M{m001:TestModule}
              §F{f001:Test:pub}
                §O{i32}
                §B{counter} 0
                §R couner
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
            §M{m001:TestModule}
              §F{f001:Test:pub}
                §O{i32}
                §B{counter} 0
                §R xyz
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
            §M{m001:TestModule}
              §F{f001:Add:pub}
                §I{i32:value}
                §O{i32}
                §R valeu
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
            §M{m001:TestModule}
              §F{f001:Helper:pub}
                §O{i32}
                §R 42
              §F{f002:Main:pub}
                §O{i32}
                §B{x} Helper
                §R x
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
            §M{m001:TestModule}
              §F{f001:Add:pub}
                §I{i32:x}
                §I{i32:x}
                §O{i32}
                §R x
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
            §M{m001:TestModule}
              §F{f001:Calc:pub}
                §O{i32}
                §B{counter} 0
                §B{result} couner
                §R reslt
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
            §M{m001:TestModule}
              §F{f001:Test:pub}
                §O{i32}
                §B{counter} 0
                §R couner
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
    public void OperatorTypo_GeneratesFix()
    {
        var source = """
            §M{m001:TestModule}
              §F{f001:Test:pub}
                §O{bool}
                §R (cotains "hello" "h")
            """;

        var fixes = LspTestHarness.GetDiagnosticsWithFixes(source);

        Assert.NotEmpty(fixes);
        var operatorFix = fixes.FirstOrDefault(f => f.Code == "Calor0106");
        Assert.NotNull(operatorFix);
        Assert.Contains("contains", operatorFix.Fix.Description);
        Assert.Single(operatorFix.Fix.Edits);
        Assert.Equal("contains", operatorFix.Fix.Edits[0].NewText);
    }

    [Fact]
    public void OperatorTypo_AppliedFix_ProducesValidCode()
    {
        var source = """
            §M{m001:TestModule}
              §F{f001:Test:pub}
                §O{bool}
                §R (cotains "hello" "h")
            """;

        var fixes = LspTestHarness.GetDiagnosticsWithFixes(source);
        var fix = fixes.First(f => f.Code == "Calor0106");

        // Apply the fix manually
        var fixedSource = source.Replace("cotains", fix.Fix.Edits[0].NewText);

        // Verify the fixed source compiles
        var diagnostics = LspTestHarness.GetDiagnostics(fixedSource);
        Assert.DoesNotContain(diagnostics, d => d.Code == "Calor0106");
    }

    [Fact]
    public void NameofOperator_CompilesWithNoErrors()
    {
        var source = """
            §M{m001:TestModule}
              §F{f001:Test:pub}
                §O{str}
                §R (nameof x)
            """;

        var diagnostics = LspTestHarness.GetDiagnostics(source);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void UnknownOperator_NoSimilar_NoFix()
    {
        var source = """
            §M{m001:TestModule}
              §F{f001:Test:pub}
                §O{i32}
                §R (xyzqwerty 1 2)
            """;

        var diagnostics = LspTestHarness.GetDiagnostics(source);

        Assert.True(diagnostics.HasErrors);
        var error = diagnostics.First(d => d.Code == "Calor0106");
        Assert.Contains("xyzqwerty", error.Message);
        Assert.Contains("arithmetic", error.Message); // Shows valid operator categories

        // No fix expected for completely unknown operators
        var fixes = LspTestHarness.GetDiagnosticsWithFixes(source);
        var operatorFix = fixes.FirstOrDefault(f => f.Code == "Calor0106");
        Assert.Null(operatorFix);
    }

    #endregion
}
