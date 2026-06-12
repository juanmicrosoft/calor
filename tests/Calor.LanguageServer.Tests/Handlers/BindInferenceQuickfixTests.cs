using Calor.LanguageServer.Tests.Helpers;
using Xunit;

namespace Calor.LanguageServer.Tests.Handlers;

/// <summary>
/// Verifies the LSP surfaces SuggestedFix quick-fixes for strict
/// bind-inference diagnostics (Calor0251-0253), promoted to default-on
/// in v0.6.3 (RFC v0.6 bind-inference-formalization §6 Phase 4).
/// </summary>
public class BindInferenceQuickfixTests
{
    [Fact]
    public void Calor0251_NoneInitializer_SurfacesOptionFix()
    {
        var source = """
            §M{m001:Test}
              §F{f001:Foo:pub}
                §O{void}
                §B{x} §NN
            """;

        var fixes = LspTestHarness.GetDiagnosticsWithFixes(source);

        var fix = Assert.Single(fixes, f => f.Code == "Calor0251");
        Assert.Single(fix.Fix.Edits);
        Assert.Equal(":Option<object>", fix.Fix.Edits[0].NewText);
        Assert.Contains("Option<object>", fix.Fix.Description);
    }

    [Fact]
    public void Calor0252_VecEmpty_SurfacesVecFix()
    {
        var source = """
            §M{m001:Test}
              §F{f001:Foo:pub}
                §O{void}
                §B{xs} §C{Vec.empty}
            """;

        var fixes = LspTestHarness.GetDiagnosticsWithFixes(source);

        var fix = Assert.Single(fixes, f => f.Code == "Calor0252");
        Assert.Equal(":Vec<object>", fix.Fix.Edits[0].NewText);
    }

    [Fact]
    public void Calor0253_AmbiguousNumeric_SurfacesF64Fix()
    {
        var source = """
            §M{m001:Test}
              §F{f001:Foo:pub}
                §O{void}
                §B{x} (+ INT:0 FLOAT:0.0)
            """;

        var fixes = LspTestHarness.GetDiagnosticsWithFixes(source);

        var fix = Assert.Single(fixes, f => f.Code == "Calor0253");
        Assert.Equal(":f64", fix.Fix.Edits[0].NewText);
    }

    [Fact]
    public void TypedBinding_NoStrictDiagnosticOrFix()
    {
        // Explicit type annotation bypasses strict-inference checks; the LSP
        // should not surface any Calor0251-0253 quick-fixes.
        var source = """
            §M{m001:Test}
              §F{f001:Foo:pub}
                §O{void}
                §B{x:Option<i32>} §NN
            """;

        var fixes = LspTestHarness.GetDiagnosticsWithFixes(source);

        Assert.DoesNotContain(fixes, f => f.Code is "Calor0251" or "Calor0252" or "Calor0253");
    }
}
