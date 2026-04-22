using Calor.Compiler.Analysis.BugPatterns.Patterns;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests.Analysis;

/// <summary>
/// Tests for <see cref="OptionResultFlowChecker"/>, reconstructed as part of the
/// TIER1A post-mortem's §6 shape-Calor corpus experiment.
///
/// The checker is expected to:
///   - flag all the cases the existing <see cref="NullDereferenceChecker"/> does,
///   - additionally flag reassignment-after-check,
///   - additionally NOT flag the guard-return pattern (fewer false positives).
///
/// Each test parses Calor source, binds it, and invokes the checker directly.
/// Tests fall through quietly when the parser can't accept a given shape — the same
/// pattern as existing bug-pattern tests.
/// </summary>
public class OptionResultFlowCheckerTests
{
    private static BoundFunction? Parse(string source, out DiagnosticBag parseDiag)
    {
        parseDiag = new DiagnosticBag();
        var tokens = new Lexer(source, parseDiag).TokenizeAll();
        if (parseDiag.HasErrors) return null;
        var module = new Parser(tokens, parseDiag).Parse();
        if (parseDiag.HasErrors) return null;
        var binder = new Binder(parseDiag);
        var bound = binder.Bind(module);
        if (parseDiag.HasErrors || bound.Functions.Count == 0) return null;
        return bound.Functions[0];
    }

    private static IReadOnlyList<Diagnostic> Check(BoundFunction func)
    {
        var diag = new DiagnosticBag();
        new OptionResultFlowChecker().Check(func, diag);
        return diag.Where(d => d.Code == DiagnosticCode.UnsafeUnwrapFlow).ToList();
    }

    // ========================================================================
    // Positive cases — the checker should fire
    // ========================================================================

    [Fact]
    public void Unwrap_WithoutCheck_Fires()
    {
        var src = @"
§M{m001:Test}
§F{f001:Direct:pub}
  §I{Option<i32>:opt}
  §O{i32}
  §R (CALL opt.unwrap)
§/F{f001}
§/M{m001}";

        var func = Parse(src, out var pd);
        Assert.True(func != null, "Parser did not accept test source: " + string.Join("; ", pd.Select(d => d.Message)));

        var findings = Check(func!);
        Assert.NotEmpty(findings);
        Assert.Contains(findings, f => f.Message.Contains("opt"));
    }

    [Fact]
    public void Unwrap_OnResult_Fires()
    {
        var src = @"
§M{m001:Test}
§F{f001:DirectResult:pub}
  §I{Result<i32,Error>:res}
  §O{i32}
  §R (CALL res.unwrap)
§/F{f001}
§/M{m001}";

        var func = Parse(src, out var pd);
        Assert.True(func != null, "Parser did not accept test source: " + string.Join("; ", pd.Select(d => d.Message)));

        var findings = Check(func!);
        Assert.NotEmpty(findings);
        Assert.Contains(findings, f => f.Message.Contains("res"));
    }

    [Fact]
    public void Expect_WithoutCheck_Fires()
    {
        var src = @"
§M{m001:Test}
§F{f001:Expect:pub}
  §I{Option<i32>:opt}
  §O{i32}
  §R (CALL opt.expect)
§/F{f001}
§/M{m001}";

        var func = Parse(src, out var pd);
        Assert.True(func != null, "Parser did not accept test source: " + string.Join("; ", pd.Select(d => d.Message)));

        Assert.NotEmpty(Check(func!));
    }

    // ========================================================================
    // Negative cases — the checker should NOT fire
    // ========================================================================

    [Fact]
    public void UnwrapOr_IsSafe_DoesNotFire()
    {
        var src = @"
§M{m001:Test}
§F{f001:Safe:pub}
  §I{Option<i32>:opt}
  §O{i32}
  §R (CALL opt.unwrap_or INT:0)
§/F{f001}
§/M{m001}";

        var func = Parse(src, out var pd);
        Assert.True(func != null, "Parser did not accept test source: " + string.Join("; ", pd.Select(d => d.Message)));

        Assert.Empty(Check(func!));
    }

    [Fact]
    public void Unwrap_AfterIsSomeCheck_DoesNotFire()
    {
        var src = @"
§M{m001:Test}
§F{f001:Checked:pub}
  §I{Option<i32>:opt}
  §O{i32}
  §IF{if1} (CALL opt.is_some)
    §R (CALL opt.unwrap)
  §/I{if1}
  §R INT:0
§/F{f001}
§/M{m001}";

        var func = Parse(src, out var pd);
        Assert.True(func != null, "Parser did not accept test source: " + string.Join("; ", pd.Select(d => d.Message)));

        Assert.Empty(Check(func!));
    }

    [Fact]
    public void Unwrap_OutsideIsSomeCheck_Fires()
    {
        var src = @"
§M{m001:Test}
§F{f001:OutsideCheck:pub}
  §I{Option<i32>:opt}
  §O{i32}
  §IF{if1} (CALL opt.is_some)
    §R INT:1
  §/I{if1}
  §R (CALL opt.unwrap)
§/F{f001}
§/M{m001}";

        var func = Parse(src, out var pd);
        Assert.True(func != null, "Parser did not accept test source: " + string.Join("; ", pd.Select(d => d.Message)));

        // After the if, the check doesn't hold — outside-branch unwrap is unsafe.
        // NOTE: guard-return pattern doesn't apply because the condition was is_some
        // (not is_none), so the else-implicit case is unchecked.
        Assert.NotEmpty(Check(func!));
    }

    // ========================================================================
    // New behavior #1 — reassignment invalidation
    // ========================================================================

    [Fact]
    public void Unwrap_AfterReassignment_Fires()
    {
        var src = @"
§M{m001:Test}
§F{f001:Reassign:pub}
  §I{Option<i32>:newValue}
  §O{i32}
  §B{~opt:Option<i32>} (CALL getOption)
  §IF{if1} (CALL opt.is_some)
    §ASSIGN opt newValue
    §R (CALL opt.unwrap)
  §/I{if1}
  §R INT:0
§/F{f001}
§/M{m001}";

        var func = Parse(src, out var pd);
        Assert.True(func != null, "Parser did not accept test source: " + string.Join("; ", pd.Select(d => d.Message)));

        // Inside the if, opt was checked, then reassigned — the unwrap is unsafe.
        Assert.NotEmpty(Check(func!));
    }

    // ========================================================================
    // New behavior #2 — guard-return pattern
    // ========================================================================

    [Fact]
    public void Unwrap_AfterGuardReturn_DoesNotFire()
    {
        var src = @"
§M{m001:Test}
§F{f001:Guarded:pub}
  §I{Option<i32>:opt}
  §O{i32}
  §IF{if1} (CALL opt.is_none)
    §R INT:0
  §/I{if1}
  §R (CALL opt.unwrap)
§/F{f001}
§/M{m001}";

        var func = Parse(src, out var pd);
        Assert.True(func != null, "Parser did not accept test source: " + string.Join("; ", pd.Select(d => d.Message)));

        // Guard-return: if None, return; fallthrough means opt is Some.
        Assert.Empty(Check(func!));
    }

    [Fact]
    public void Unwrap_AfterGuardThrow_DoesNotFire()
    {
        var src = @"
§M{m001:Test}
§F{f001:GuardedThrow:pub}
  §I{Option<i32>:opt}
  §O{i32}
  §IF{if1} (CALL opt.is_none)
    §TH STR:""no value""
  §/I{if1}
  §R (CALL opt.unwrap)
§/F{f001}
§/M{m001}";

        var func = Parse(src, out var pd);
        Assert.True(func != null, "Parser did not accept test source: " + string.Join("; ", pd.Select(d => d.Message)));

        Assert.Empty(Check(func!));
    }

    [Fact]
    public void Unwrap_AfterIncompleteGuard_Fires()
    {
        var src = @"
§M{m001:Test}
§F{f001:IncompleteGuard:pub}
  §I{Option<i32>:opt}
  §O{i32}
  §IF{if1} (CALL opt.is_none)
    §C{log.warn} §A STR:""oops"" §/C
  §/I{if1}
  §R (CALL opt.unwrap)
§/F{f001}
§/M{m001}";

        var func = Parse(src, out var pd);
        Assert.True(func != null, "Parser did not accept test source: " + string.Join("; ", pd.Select(d => d.Message)));

        // If-body does not exit — fallthrough reaches the unwrap on an unchecked opt.
        Assert.NotEmpty(Check(func!));
    }

    // ========================================================================
    // Anonymous receivers
    // ========================================================================

    [Fact]
    public void Unwrap_OnAnonymousExpression_DoesNotFire()
    {
        // Unwrap on a direct call result (not a variable) has no receiver name to
        // track; the flow checker skips these. Orthogonal coverage from the
        // existing NullDereferenceChecker can still flag this.
        var src = @"
§M{m001:Test}
§F{f001:Anonymous:pub}
  §O{i32}
  §R (CALL (CALL getOption).unwrap)
§/F{f001}
§/M{m001}";

        var func = Parse(src, out var pd);
        Assert.True(func != null, "Parser did not accept test source: " + string.Join("; ", pd.Select(d => d.Message)));

        Assert.Empty(Check(func!));
    }
}
