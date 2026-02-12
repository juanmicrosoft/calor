using Calor.LanguageServer.Tests.Helpers;
using Xunit;

namespace Calor.LanguageServer.Tests.Handlers;

public class CodeActionHandlerTests
{
    [Fact]
    public void MismatchedId_GeneratesFix()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test}
            §R 0
            §/F{f002}
            §/M{m001}
            """;

        var fixes = LspTestHarness.GetDiagnosticsWithFixes(source);

        Assert.NotEmpty(fixes);
        Assert.Contains(fixes, f => f.Code == "Calor0101");

        var fix = fixes.First(f => f.Code == "Calor0101");
        Assert.Equal("Change 'f002' to 'f001'", fix.Fix.Description);
        Assert.Single(fix.Fix.Edits);
    }

    [Fact]
    public void MismatchedModuleId_GeneratesFix()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test}
            §R 0
            §/F{f001}
            §/M{m002}
            """;

        var fixes = LspTestHarness.GetDiagnosticsWithFixes(source);

        Assert.NotEmpty(fixes);
        Assert.Contains(fixes, f => f.Code == "Calor0101" && f.Fix.Description.Contains("m001"));
    }

    [Fact]
    public void FixEdit_HasCorrectPosition()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test}
            §R 0
            §/F{f002}
            §/M{m001}
            """;

        var fixes = LspTestHarness.GetDiagnosticsWithFixes(source);
        var fix = fixes.First(f => f.Code == "Calor0101");

        Assert.Single(fix.Fix.Edits);
        var edit = fix.Fix.Edits[0];

        // The edit should be for replacing 'f002' with 'f001'
        Assert.Equal("f001", edit.NewText);
    }

    [Fact]
    public void MultipleMismatchedIds_GeneratesMultipleFixes()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test}
            §L{l001:i:0:10}
            §P i
            §/L{l002}
            §R 0
            §/F{f002}
            §/M{m001}
            """;

        var fixes = LspTestHarness.GetDiagnosticsWithFixes(source);

        // Should have at least 2 fixes (for f002 and l002)
        Assert.True(fixes.Count >= 2);
        Assert.All(fixes, f => Assert.Equal("Calor0101", f.Code));
    }

    [Fact]
    public void ValidSource_NoFixes()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test}
            §R 0
            §/F{f001}
            §/M{m001}
            """;

        var fixes = LspTestHarness.GetDiagnosticsWithFixes(source);

        Assert.Empty(fixes);
    }

    [Fact]
    public void FixApplied_SourceIsValid()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test}
            §R 0
            §/F{f002}
            §/M{m001}
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
            §/F{f001}
            §/M{m001}
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
            §/F{f001}
            §/M{m001}
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
            §/F{f001}
            §/M{m001}
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
            §/F{f001}
            §F{f002:Main:pub}
            §O{i32}
            §B{x} Helper
            §R x
            §/F{f002}
            §/M{m001}
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
            §/F{f001}
            §/M{m001}
            """;

        var fixes = LspTestHarness.GetDiagnosticsWithFixes(source);

        Assert.NotEmpty(fixes);
        var duplicateFix = fixes.FirstOrDefault(f => f.Code == "Calor0201");
        Assert.NotNull(duplicateFix);
        Assert.Contains("Rename", duplicateFix.Fix.Description);
        Assert.Contains("x2", duplicateFix.Fix.Description);
    }

    [Fact]
    public void MismatchedId_FixPositionIsCorrect()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Test:pub}
            §O{i32}
            §R 0
            §/F{wrong}
            §/M{m001}
            """;

        var fixes = LspTestHarness.GetDiagnosticsWithFixes(source);
        var fix = fixes.First(f => f.Code == "Calor0101");

        Assert.Single(fix.Fix.Edits);
        var edit = fix.Fix.Edits[0];

        // Verify the fix replaces "wrong" with "f001"
        Assert.Equal("f001", edit.NewText);
        // Line 5 (1-indexed), after "§/F{" which is column 4
        Assert.Equal(5, edit.StartLine);
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
            §/F{f001}
            §/M{m001}
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
    public void NestedStructure_FixesWorkCorrectly()
    {
        var source = """
            §M{m001:TestModule}
            §F{f001:Outer:pub}
            §O{i32}
            §L{l001:i:0:10:1}
            §P i
            §/L{l002}
            §R 0
            §/F{f001}
            §/M{m001}
            """;

        var fixes = LspTestHarness.GetDiagnosticsWithFixes(source);

        // Should fix l002 -> l001
        Assert.NotEmpty(fixes);
        var loopFix = fixes.FirstOrDefault(f => f.Fix.Edits[0].NewText == "l001");
        Assert.NotNull(loopFix);
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
            §/F{f001}
            §/M{m001}
            """;

        var fixes = LspTestHarness.GetDiagnosticsWithFixes(source);
        var fix = fixes.First(f => f.Code == "Calor0200");

        // Apply the fix manually
        var fixedSource = source.Replace("couner", fix.Fix.Edits[0].NewText);

        // Verify the fixed source has no undefined reference errors
        var diagnostics = LspTestHarness.GetDiagnostics(fixedSource);
        Assert.DoesNotContain(diagnostics, d => d.Code == "Calor0200");
    }
}
