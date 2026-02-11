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
}
