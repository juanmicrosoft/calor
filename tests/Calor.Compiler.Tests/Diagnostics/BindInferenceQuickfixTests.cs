using Xunit;
using DiagnosticCode = Calor.Compiler.Diagnostics.DiagnosticCode;
using SuggestedFix = Calor.Compiler.Diagnostics.SuggestedFix;

namespace Calor.Compiler.Tests.Diagnostics;

/// <summary>
/// LSP code-action quick-fix coverage for the strict bind-inference
/// diagnostics promoted to default-on in v0.6.3 (RFC v0.6 §6 Phase 4):
/// Calor0251 (none/null), Calor0252 (generic factory), Calor0253
/// (ambiguous numeric). Each diagnostic now carries a
/// <see cref="SuggestedFix"/> that inserts the recommended <c>:type</c>
/// annotation before the closing <c>}</c> of the bind's attribute block.
/// </summary>
public class BindInferenceQuickfixTests
{
    [Fact]
    public void Calor0251_None_AttachesOptionFix()
    {
        var source = """
            §M{m001:Test}
              §F{f001:Foo:pub}
                  §O{void}
                  §B{x} §NN
            """;

        var result = Program.Compile(source, "test.calr",
            new CompilationOptions { StrictBindInference = true });

        var withFix = Assert.Single(result.Diagnostics.DiagnosticsWithFixes,
            d => d.Code == DiagnosticCode.BindCannotInferNullLiteral);
        var edit = Assert.Single(withFix.Fix.Edits);
        Assert.Equal(":Option<object>", edit.NewText);
        Assert.Contains("Option<object>", withFix.Fix.Description);

        // Insertion must land at the column of the closing '}' of '§B{x}'.
        var fixedSource = ApplyFix(source, withFix.Fix);
        Assert.Contains("§B{x:Option<object>} §NN", fixedSource);
    }

    [Fact]
    public void Calor0251_Null_AttachesNullableFix()
    {
        var source = """
            §M{m001:Test}
              §F{f001:Foo:pub}
                  §O{void}
                  §B{x} null
            """;

        var result = Program.Compile(source, "test.calr",
            new CompilationOptions { StrictBindInference = true });

        var withFix = Assert.Single(result.Diagnostics.DiagnosticsWithFixes,
            d => d.Code == DiagnosticCode.BindCannotInferNullLiteral);
        var edit = Assert.Single(withFix.Fix.Edits);
        Assert.Equal(":object?", edit.NewText);

        var fixedSource = ApplyFix(source, withFix.Fix);
        Assert.Contains("§B{x:object?} null", fixedSource);
    }

    [Fact]
    public void Calor0252_VecEmpty_AttachesVecFix()
    {
        var source = """
            §M{m001:Test}
              §F{f001:Foo:pub}
                  §O{void}
                  §B{xs} §C{Vec.empty}
            """;

        var result = Program.Compile(source, "test.calr",
            new CompilationOptions { StrictBindInference = true });

        var withFix = Assert.Single(result.Diagnostics.DiagnosticsWithFixes,
            d => d.Code == DiagnosticCode.BindCannotInferGenericReturn);
        var edit = Assert.Single(withFix.Fix.Edits);
        Assert.Equal(":Vec<object>", edit.NewText);

        var fixedSource = ApplyFix(source, withFix.Fix);
        Assert.Contains("§B{xs:Vec<object>} §C{Vec.empty}", fixedSource);
    }

    [Fact]
    public void Calor0252_MapEmpty_AttachesMapFixWithTwoParams()
    {
        var source = """
            §M{m001:Test}
              §F{f001:Foo:pub}
                  §O{void}
                  §B{m} §C{Map.empty}
            """;

        var result = Program.Compile(source, "test.calr",
            new CompilationOptions { StrictBindInference = true });

        var withFix = Assert.Single(result.Diagnostics.DiagnosticsWithFixes,
            d => d.Code == DiagnosticCode.BindCannotInferGenericReturn);
        var edit = Assert.Single(withFix.Fix.Edits);
        Assert.Equal(":Map<object, object>", edit.NewText);
    }

    [Fact]
    public void Calor0253_AmbiguousNumeric_AttachesF64Fix()
    {
        var source = """
            §M{m001:Test}
              §F{f001:Foo:pub}
                  §O{void}
                  §B{x} (+ INT:0 FLOAT:0.0)
            """;

        var result = Program.Compile(source, "test.calr",
            new CompilationOptions { StrictBindInference = true });

        var withFix = Assert.Single(result.Diagnostics.DiagnosticsWithFixes,
            d => d.Code == DiagnosticCode.BindAmbiguousNumeric);
        var edit = Assert.Single(withFix.Fix.Edits);
        Assert.Equal(":f64", edit.NewText);

        var fixedSource = ApplyFix(source, withFix.Fix);
        Assert.Contains("§B{x:f64} (+ INT:0 FLOAT:0.0)", fixedSource);
    }

    [Fact]
    public void Calor0251_Mutable_AttachesFixAtCorrectColumn()
    {
        // The fix's insertion column must account for the leading '~' on
        // mutable bindings; column math is (Span.Column + 3 + 1 + name.Length).
        var source = """
            §M{m001:Test}
              §F{f001:Foo:pub}
                  §O{void}
                  §B{~items} §NN
            """;

        var result = Program.Compile(source, "test.calr",
            new CompilationOptions { StrictBindInference = true });

        var withFix = Assert.Single(result.Diagnostics.DiagnosticsWithFixes,
            d => d.Code == DiagnosticCode.BindCannotInferNullLiteral);
        var fixedSource = ApplyFix(source, withFix.Fix);
        Assert.Contains("§B{~items:Option<object>} §NN", fixedSource);
    }

    [Fact]
    public void MultipleBindings_EachGetsItsOwnFix()
    {
        var source = """
            §M{m001:Test}
              §F{f001:Foo:pub}
                  §O{void}
                  §B{a} §NN
                  §B{b} §C{Vec.empty}
                  §B{c} (+ INT:1 FLOAT:1.0)
            """;

        var result = Program.Compile(source, "test.calr",
            new CompilationOptions { StrictBindInference = true });

        var fixes = result.Diagnostics.DiagnosticsWithFixes
            .Where(d => d.Code is DiagnosticCode.BindCannotInferNullLiteral
                              or DiagnosticCode.BindCannotInferGenericReturn
                              or DiagnosticCode.BindAmbiguousNumeric)
            .ToList();
        Assert.Equal(3, fixes.Count);
        Assert.All(fixes, f => Assert.Single(f.Fix.Edits));
        Assert.All(fixes, f => Assert.StartsWith(":", f.Fix.Edits[0].NewText));
    }

    [Fact]
    public void NoStrict_NoDiagnosticAndNoFix()
    {
        var source = """
            §M{m001:Test}
              §F{f001:Foo:pub}
                  §O{void}
                  §B{x} §NN
            """;

        var result = Program.Compile(source, "test.calr",
            new CompilationOptions { StrictBindInference = false });

        Assert.DoesNotContain(result.Diagnostics.DiagnosticsWithFixes,
            d => d.Code == DiagnosticCode.BindCannotInferNullLiteral);
    }

    // ApplyFix mirrors SuggestionTests.cs:ApplyFix — 1-indexed columns,
    // applied in reverse order to avoid position shifts.
    private static string ApplyFix(string source, SuggestedFix fix)
    {
        var lines = source.Split('\n');
        foreach (var edit in fix.Edits.OrderByDescending(e => e.StartLine).ThenByDescending(e => e.StartColumn))
        {
            var startLineIdx = edit.StartLine - 1;
            var endLineIdx = edit.EndLine - 1;
            if (startLineIdx < 0 || startLineIdx >= lines.Length)
            {
                continue;
            }
            if (startLineIdx == endLineIdx)
            {
                var line = lines[startLineIdx];
                var startCol = Math.Min(edit.StartColumn - 1, line.Length);
                var endCol = Math.Min(edit.EndColumn - 1, line.Length);
                if (startCol >= 0 && endCol >= startCol)
                {
                    lines[startLineIdx] = line.Substring(0, startCol) + edit.NewText + line.Substring(endCol);
                }
            }
        }
        return string.Join('\n', lines);
    }
}
