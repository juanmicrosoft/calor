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
    public void RemovedNone_DoesNotSuggestRemovedType()
    {
        var source = """
            §M{Test}
              §F{Foo:pub}
                  §O{void}
                  §B{x} §NN
            """;

        var result = Program.Compile(source, "test.calr",
            new CompilationOptions { StrictBindInference = true });

        Assert.True(result.HasErrors);
        Assert.DoesNotContain(result.Diagnostics.DiagnosticsWithFixes,
            d => d.Code == DiagnosticCode.BindCannotInferNullLiteral);
    }

    [Fact]
    public void Calor0252_MapEmpty_AttachesMapFixWithTwoParams()
    {
        var source = """
            module Test Test block
              func Foo () → void
                !
                let m = Map.empty()
            """;

        var result = Program.Compile(source, "test.calr",
            new CompilationOptions { StrictBindInference = true });

        var withFix = Assert.Single(result.Diagnostics.DiagnosticsWithFixes,
            d => d.Code == DiagnosticCode.BindCannotInferGenericReturn);
        var edit = Assert.Single(withFix.Fix.Edits);
        Assert.Equal(":Map<object, object>", edit.NewText);
    }

    [Fact]
    public void MultipleBindings_EachGetsItsOwnFix()
    {
        var source = """
            module Test Test block
              func Foo () → void
                !
                let a = null
                let b = Vec.empty()
                let c = (1 + 1.0)
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
            module Test Test block
              func Foo () → void
                !
                let x = null
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
