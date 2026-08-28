using System.Text;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Enforcement.Tests;

/// <summary>
/// #1104 (v0.16 MUST W3(c)) — the nested <c>EffectInferrer</c>'s AST-side
/// local-type resolution is BOUNDED: a visited set keyed on the name being
/// resolved, plus a frame cap as a backstop
/// (<see cref="EffectEnforcementPass.AstResolutionBound"/>). A cycle resolves
/// to the unknown-type sentinel instead of recursing.
/// </summary>
/// <remarks>
/// <para>Every test here enforces the PARSED module directly —
/// <c>new EffectEnforcementPass(bag).Enforce(module)</c> with no binder in
/// front of it. That is the reproduction path from the issue, and it is not
/// hypothetical: <see cref="EditPreviewTool.CheckEffects"/> (the MCP server's
/// <c>edit_preview</c>) runs the pass on a lex/parse-only <c>ParseResult</c>
/// with no binder anywhere on the path, so <c>calor mcp</c> died on any parsed
/// <c>.calr</c> of this shape. The in-process ledgers (the Calor0425 corpus
/// ledger, K1) are the second unbound caller. The CLI binds first and returns
/// on binding errors, which is why it never saw this.</para>
///
/// <para><b>Discriminating revert</b> (roadmap §3.1 W3(c), verbatim): "revert
/// (c) → the recursion test crashes the host". A StackOverflowException is a
/// fail-fast — no catch observes it — so the crash-repro pins here do not go
/// red, they take the test host down: a regression surfaces as an ABORTED
/// assembly (every test in it dies, not just these) plus an
/// <c>eng/test-manifest.json</c> count mismatch, never as a named red test.</para>
///
/// <para><b>Gate 6 / §9 (no resolution answer changes on acyclic code
/// shallower than the cap)</b>: the visited set fires only when a name is asked
/// for while its own resolution is on the stack, which is a cycle by
/// construction. The cap is chosen an order of magnitude above what the corpus
/// reaches (13 frames over the 364 corpus modules, pinned by
/// <see cref="EffectInferrerCorpusDepthTests"/>; default
/// <see cref="EffectEnforcementPass.AstResolutionBound.DefaultDepthCap"/> =
/// 224). Code deeper than the cap does change answers — that is the backstop
/// working, and the cap tests below say exactly where the line is.</para>
/// </remarks>
public class EffectInferrerRecursionTests
{
    private const string FixturePath = "Effects/Issue1104_BatchingSink_LoopAsync.calr";

    /// <summary>
    /// Frames one link of a FLAT chain costs: the name ask itself, plus the
    /// one structural level <c>FindLocalDeclarationType</c> walks to find the
    /// <c>§B</c> at the top of the body. Nesting a link inside a <c>§IF</c>
    /// adds one structural frame per level, which is the whole point of C1.
    /// </summary>
    private const int FramesPerFlatLink = 2;

    // ===== crash-repro pins over the committed Serilog-reduced fixture =====

    [Fact]
    public void SerilogFixture_EnforcedWithoutBinding_CompletesInsteadOfCrashingTheHost()
    {
        // The issue's exact API: a default-constructed pass over the parsed,
        // UNBOUND module. Before the fix this line killed the process.
        var module = Parse(TestHarness.LoadScenario(FixturePath));
        var bag = new DiagnosticBag();

        new EffectEnforcementPass(bag).Enforce(module);

        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.AnalysisICE);
    }

    [Fact]
    public void SerilogFixture_ThroughMcpEditPreview_CompletesInsteadOfCrashingTheHost()
    {
        // Review round 2, MAJOR M1 — the SHIPPING unbound caller.
        // EditPreviewTool.CheckEffects runs the effect pass on a lex/parse-only
        // ParseResult (CalorSourceHelper.Parse; no binder on that path), so
        // `calor mcp`'s edit_preview aborted on any parsed .calr of this shape.
        // Driven through the real entry point, not a re-implementation of it,
        // so a refactor that reintroduces an unbounded pass here is caught.
        var parse = CalorSourceHelper.Parse(
            TestHarness.LoadScenario(FixturePath), "Issue1104.calr");
        Assert.True(parse.IsSuccess);

        var result = new EditPreviewTool.EffectCheckResult();
        EditPreviewTool.CheckEffects(parse, result);

        // Permissive policy on this path: no violations to report, and — the
        // claim that matters — the host is still alive to assert it.
        Assert.False(result.HasViolations);
        Assert.Empty(result.EffectViolations);
    }

    [Fact]
    public void SerilogFixture_CycleResolvesToSentinel_ByVisitedSetNotByCap()
    {
        var (bag, bound) = Enforce(TestHarness.LoadScenario(FixturePath));

        // The hoisted `out var` binding is the name whose own resolution was
        // re-entered; it resolved by the VISITED SET, never by the backstop.
        Assert.True(bound.CycleStops >= 1, $"expected a cycle stop, got {bound.CycleStops}");
        Assert.Equal(0, bound.DepthCapStops);
        Assert.All(bound.StoppedNames, name => Assert.Equal("shouldDropQueue", name));
        // Two asks deep — the binding, then its call's RECEIVER (the
        // `_batchScheduler` field) — each costing an ask frame plus the one
        // structural level that finds the declaration. The re-ask of
        // `shouldDropQueue` is refused before it takes a frame.
        Assert.Equal(2 * FramesPerFlatLink, bound.MaxObservedDepth);
        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.AnalysisICE);
    }

    [Fact]
    public void SerilogFixture_CycleStopIsFailClosed_NotSilentlyPure()
    {
        // Review round 2, probe P5. "No ICE" is not the whole claim: the
        // verdict must stay CLOSED. An unresolved receiver keeps the call
        // unknown (Calor0411) and the unknown effect propagates to the
        // enclosing callable (Calor0410) — the sentinel must never be read as
        // "pure". If a future change makes a cycle resolve to a REAL type,
        // this goes red and the change gets looked at.
        var (bag, _) = Enforce(TestHarness.LoadScenario(FixturePath));

        Assert.Contains(bag, d => d.Code == DiagnosticCode.UnknownExternalCall
            && d.Message.Contains("_batchScheduler.MarkFailure", StringComparison.Ordinal));
        Assert.Contains(bag, d => d.Code == DiagnosticCode.ForbiddenEffect
            && d.Message.Contains("uses effect 'unknown'", StringComparison.Ordinal));
    }

    [Fact]
    public void SerilogFixture_IsTheReducedHoistedOutVarShape()
    {
        // Pins WHAT the fixture is, so a later edit cannot quietly replace it
        // with something that no longer exercises the cycle: a binding with no
        // initializer on its own line, followed by a call that names it as an
        // argument, on a receiver that resolves (the field).
        var source = TestHarness.LoadScenario(FixturePath);
        var module = Parse(source);

        var cls = Assert.Single(module.Classes);
        Assert.Equal("BatchingSink", cls.Name);
        Assert.Contains(cls.Fields, f => f.Name == "_batchScheduler");
        var method = Assert.Single(cls.Methods);
        var bind = Assert.IsType<BindStatementNode>(Assert.Single(method.Body));
        Assert.Equal("shouldDropQueue", bind.Name);
        var call = Assert.IsType<CallExpressionNode>(bind.Initializer);
        Assert.Equal("_batchScheduler.MarkFailure", call.Target);
        Assert.Contains(call.Arguments, a => a is ReferenceNode { Name: "shouldDropQueue" });
    }

    // ===== synthetic cycle shapes =====

    [Fact]
    public void DirectSelfCycle_ThroughArgument_ResolvesToSentinel()
    {
        // `§B{~x} = scheduler.MarkFailure(x)` — the value's type depends on its
        // own type. The parameter gives the receiver a type, so the arguments
        // ARE typed and the cycle is entered.
        var source = """
            §M{m001:SelfCycle}
              §F{f001:Loop:priv} (FailureAwareBatchScheduler:scheduler) -> void
                §B{~x} §C{scheduler.MarkFailure} §A x §/C
            """;

        var (bag, bound) = Enforce(source);

        Assert.True(bound.CycleStops >= 1);
        Assert.Equal(0, bound.DepthCapStops);
        Assert.All(bound.StoppedNames, name => Assert.Equal("x", name));
        Assert.Equal(FramesPerFlatLink, bound.MaxObservedDepth);
        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.AnalysisICE);
    }

    [Fact]
    public void DirectSelfCycle_ThroughReceiver_ResolvesToSentinel()
    {
        // The issue's suspected shape: InferKnownCallResultType →
        // FindLocalDeclarationType → ResolveLocalValueTypeFromAst → back, via
        // the RECEIVER rather than an argument: `§B{a} = a.Where()`.
        var source = """
            §M{m001:SelfCycleReceiver}
              §F{f001:Loop:priv} () -> void
                §B{a} §C{a.Where} §/C
            """;

        var (bag, bound) = Enforce(source);

        Assert.True(bound.CycleStops >= 1);
        Assert.Equal(0, bound.DepthCapStops);
        Assert.All(bound.StoppedNames, name => Assert.Equal("a", name));
        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.AnalysisICE);
    }

    [Fact]
    public void TwoNodeCycle_ResolvesToSentinel_StoppingOnlyAtTheReEnteredName()
    {
        // a's type needs b's, b's needs a's.
        var source = """
            §M{m001:TwoCycle}
              §F{f001:Loop:priv} (FailureAwareBatchScheduler:scheduler) -> void
                §B{a} §C{scheduler.First} §A b §/C
                §B{b} §C{scheduler.Second} §A a §/C
            """;

        var (bag, bound) = Enforce(source);

        Assert.True(bound.CycleStops >= 1);
        Assert.Equal(0, bound.DepthCapStops);
        Assert.All(bound.StoppedNames, name => Assert.Contains(name, new[] { "a", "b" }));
        // a → b → (a refused): two links of frames, no third.
        Assert.Equal(2 * FramesPerFlatLink, bound.MaxObservedDepth);
        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.AnalysisICE);
    }

    [Fact]
    public void ThreeNodeCycle_InsideNestedBlocks_ResolvesToSentinel()
    {
        // The search descends into nested statement bodies; a cycle spread
        // across them is still one cycle, and the nesting costs extra frames.
        var source = """
            §M{m001:ThreeCycle}
              §F{f001:Loop:priv} (FailureAwareBatchScheduler:scheduler, bool:flag) -> void
                §B{a} §C{scheduler.First} §A c §/C
                §IF{if001} flag
                  §B{b} §C{scheduler.Second} §A a §/C
                  §WH{wh001} flag
                    §B{c} §C{scheduler.Third} §A b §/C
            """;

        var (bag, bound) = Enforce(source);

        Assert.True(bound.CycleStops >= 1);
        Assert.Equal(0, bound.DepthCapStops);
        // Deeper than the flat three-link cost (6), because b and c sit one and
        // two structural levels down.
        Assert.True(bound.MaxObservedDepth > 3 * FramesPerFlatLink,
            $"nesting must cost frames; got {bound.MaxObservedDepth}");
        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.AnalysisICE);
    }

    [Fact]
    public void DiamondSharedSubChain_IsNotACycle()
    {
        // Review round 2, probe P9. Two links share one sub-chain, so the same
        // name is resolved twice on DIFFERENT branches — legitimately, not
        // cyclically. Goes red under the drop-`Remove` mutation (the visited
        // set would remember the shared name from the first branch).
        var source = """
            §M{m001:Diamond}
              §F{f001:Run:priv} (List<i32>:items) -> void
                §B{_chainWhere001} §C{items.Where} §/C
                §B{_chainWhere002} §C{_chainWhere001.Where} §/C
                §B{_chainWhere003} §C{_chainWhere001.Where} §/C
                §C{_chainWhere002.ToList} §/C
                §C{_chainWhere003.ToList} §/C
            """;

        var (bag, bound) = Enforce(source);

        Assert.Equal(0, bound.CycleStops);
        Assert.Equal(0, bound.DepthCapStops);
        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Fact]
    public void ForeachVariableResolution_IsBoundedToo_AndStillResolves()
    {
        // Review round 2, probe P7. FindForeachVariableType has its own
        // structural walk, and it hops into FindLocalDeclarationType for the
        // collection's name. Both take frames from the same counter now; on
        // ordinary code neither guard fires.
        var source = """
            §M{m001:Foreach}
              §F{f001:Run:priv} (List<i32>:items) -> void
                §B{_chainWhere001} §C{items.Where} §/C
                §EACH{fe001:_eachItem001} _chainWhere001
                  §C{_eachItem001.ToString} §/C
            """;

        var (bag, bound) = Enforce(source);

        Assert.Equal(0, bound.CycleStops);
        Assert.Equal(0, bound.DepthCapStops);
        // Three frames, and every one of them is on the §EACH path: the ask
        // for the loop variable, the §B search that does not find it, then
        // FindForeachVariableType's own walk plus its hop into
        // FindLocalDeclarationType for the COLLECTION's name. Before C1 that
        // hop and that walk were both uncounted.
        Assert.Equal(3, bound.MaxObservedDepth);
        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.AnalysisICE);
    }

    // ===== the guard is per function, and name-keyed =====

    [Fact]
    public void SameNameInTwoFunctions_OneCyclingOneNot_DoesNotContaminate()
    {
        // Review round 2, probe P3. Each function gets its own inferrer and so
        // its own visited set; a name cycling in one must not make the
        // identically-named local in another resolve to the sentinel.
        var source = """
            §M{m001:CrossFunction}
              §F{f001:Cycles:priv} (FailureAwareBatchScheduler:scheduler) -> void
                §B{~_chainWhere001} §C{scheduler.MarkFailure} §A _chainWhere001 §/C
              §F{f002:DoesNot:priv} (List<i32>:items) -> void
                §B{_chainWhere001} §C{items.Where} §/C
                §C{_chainWhere001.ToList} §/C
            """;

        var (bag, bound) = Enforce(source);

        // The cycle is reported for f001's binding only.
        Assert.True(bound.CycleStops >= 1);
        Assert.All(bound.StoppedNames, name => Assert.Equal("_chainWhere001", name));
        // f002's identically-named local still resolved: its terminal call is
        // KNOWN, so no Calor0411 names it. If the visited set leaked across
        // functions (made static, or hung off the pass) this would fire.
        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.UnknownExternalCall
            && d.Message.Contains("_chainWhere001.ToList", StringComparison.Ordinal));
    }

    [Fact]
    public void SameNameInTwoFunctions_BothAcyclic_ResolveIndependently()
    {
        // Review round 2, probe P3b — the control for the above: two functions
        // that both resolve the same name normally, neither stopping.
        var source = """
            §M{m001:CrossFunctionControl}
              §F{f001:First:priv} (List<i32>:items) -> void
                §B{_chainWhere001} §C{items.Where} §/C
                §C{_chainWhere001.ToList} §/C
              §F{f002:Second:priv} (List<i32>:items) -> void
                §B{_chainWhere001} §C{items.Where} §/C
                §C{_chainWhere001.ToList} §/C
            """;

        var (bag, bound) = Enforce(source);

        Assert.Equal(0, bound.CycleStops);
        Assert.Equal(0, bound.DepthCapStops);
        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Fact]
    public void SiblingScopesWithTheSameName_ResolveByFirstMatch_NotByScope()
    {
        // Review round 2, probe P1b. The AST search is SCOPE-BLIND: it takes
        // the first §B it meets in lexical order, wherever it sits. That is
        // pre-existing behaviour, not something #1104 introduced, and it is
        // pinned here so it is discovered by a red test rather than by a bug
        // report. The guard changes nothing about it — no stop of either kind.
        var source = """
            §M{m001:Siblings}
              §F{f001:Run:priv} (List<i32>:items, bool:flag) -> void
                §IF{if001} flag
                  §B{_chainWhere001} §C{items.Where} §/C
                §EL
                  §B{_chainWhere001} §NEW{StringBuilder} §/NEW
                §C{_chainWhere001.ToList} §/C
            """;

        var (bag, bound) = Enforce(source);

        Assert.Equal(0, bound.CycleStops);
        Assert.Equal(0, bound.DepthCapStops);
        // First match wins — the §IF branch's LINQ result — so .ToList is known.
        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.UnknownExternalCall
            && d.Message.Contains("_chainWhere001.ToList", StringComparison.Ordinal));
    }

    // ===== acyclic code below the cap is untouched =====

    [Fact]
    public void LongAcyclicChain_ResolvesAllTheWayDown_NoStopOfEitherKind()
    {
        // 40 chained temporaries — far deeper than the corpus (13 frames) and
        // well under the cap (80 frames vs 224). Must resolve normally: the
        // terminal `.ToList` is a KNOWN call (no Calor0411) and neither guard
        // fired. Pins that the bound is not "give up at depth N" on ordinary
        // code.
        const int links = 40;
        var (bag, bound) = Enforce(Chain(links));

        Assert.Equal(0, bound.CycleStops);
        Assert.Equal(0, bound.DepthCapStops);
        Assert.Equal(links * FramesPerFlatLink, bound.MaxObservedDepth);
        Assert.True(bound.MaxObservedDepth
            < EffectEnforcementPass.AstResolutionBound.DefaultDepthCap);
        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.UnknownExternalCall);
        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.AnalysisICE);
    }

    [Fact]
    public void AskingTheSameAcyclicNameTwice_IsNotACycle()
    {
        // The visited set is a stack, not a memo: a name leaves it when its
        // resolution returns. Drop the `Remove` on the way out and the second
        // ask below reports a cycle that is not there.
        var source = """
            §M{m001:Twice}
              §F{f001:Run:priv} (List<i32>:items) -> void
                §B{_chainWhere001} §C{items.Where} §/C
                §C{_chainWhere001.ToList} §/C
                §C{_chainWhere001.ToList} §/C
            """;

        var (bag, bound) = Enforce(source);

        Assert.Equal(0, bound.CycleStops);
        Assert.Equal(0, bound.DepthCapStops);
        Assert.Equal(FramesPerFlatLink, bound.MaxObservedDepth);
        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Fact]
    public void CycleAndAcyclicChainInOneFunction_OnlyTheCycleStops()
    {
        // The stop is scoped to the re-entered name; the neighbouring chain
        // still resolves and its terminal call is still known.
        var source = """
            §M{m001:Mixed}
              §F{f001:Run:priv} (FailureAwareBatchScheduler:scheduler, List<i32>:items) -> void
                §B{~x} §C{scheduler.MarkFailure} §A x §/C
                §B{_chainWhere001} §C{items.Where} §/C
                §B{_chainWhere002} §C{_chainWhere001.Where} §/C
                §C{_chainWhere002.ToList} §/C
            """;

        var (bag, bound) = Enforce(source);

        Assert.True(bound.CycleStops >= 1);
        Assert.All(bound.StoppedNames, name => Assert.Equal("x", name));
        Assert.Equal(0, bound.DepthCapStops);
        Assert.Equal(2 * FramesPerFlatLink, bound.MaxObservedDepth);
        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.UnknownExternalCall
            && d.Message.Contains("_chainWhere002.ToList", StringComparison.Ordinal));
    }

    // ===== the frame cap: the C1 shape, the boundary, the default =====

    [Fact]
    public void DeepNesting_UnderTheOldAskCount_DoesNotExhaustTheStack()
    {
        // Review round 1, CRITICAL C1 — the probe that broke the first cut.
        // 50 links, each nested one §IF deeper than the last: no cycle, all
        // names distinct, and only 50 name ASKS — which the ask-counting bound
        // waved through under a cap of 64 while the structural walk it did not
        // count spent 1375 frames and overflowed a 1 MB stack.
        //
        // Frame-accurate now: the cap fires, resolution declines, and the pass
        // COMPLETES. The claim is the completion; the cap stops are how it is
        // achieved, and MaxObservedDepth never exceeding the cap is the bound
        // doing its job.
        var (bag, bound) = Enforce(NestedChain(links: 50));

        Assert.True(bound.DepthCapStops >= 1,
            "the deep-nesting shape must reach the cap — that is what makes it safe");
        Assert.Equal(EffectEnforcementPass.AstResolutionBound.DefaultDepthCap,
            bound.MaxObservedDepth);
        Assert.Equal(0, bound.CycleStops);
        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.AnalysisICE);
    }

    [Fact]
    public void DeepNesting_AtTwoHundredLinks_StillCompletesUnderTheCap()
    {
        // The 200-link version of the same shape — 4× the probe above, and the
        // one the default was derived from (cap 448 survives a 1 MB thread,
        // 480 does not; the default is half of 448). It must complete with the
        // frame ceiling respected.
        var (bag, bound) = Enforce(NestedChain(links: 200));

        Assert.True(bound.DepthCapStops >= 1);
        Assert.Equal(EffectEnforcementPass.AstResolutionBound.DefaultDepthCap,
            bound.MaxObservedDepth);
        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.AnalysisICE);
    }

    [Fact]
    public void ShallowNesting_WellUnderTheCap_ResolvesNormally()
    {
        // The control for the two above: the same nested shape at corpus scale
        // resolves completely, so "nested" is not itself what trips the cap.
        var (bag, bound) = Enforce(NestedChain(links: 5));

        Assert.Equal(0, bound.DepthCapStops);
        Assert.Equal(0, bound.CycleStops);
        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Fact]
    public void DepthCap_ChainNeedingExactlyCapFrames_Resolves()
    {
        // A flat chain of N links costs exactly 2N frames. With the cap set to
        // 2N the deepest frame is admitted and the terminal call is known.
        const int links = 8;
        var (bag, bound) = Enforce(Chain(links), cap: links * FramesPerFlatLink);

        Assert.Equal(links * FramesPerFlatLink, bound.MaxObservedDepth);
        Assert.Equal(0, bound.DepthCapStops);
        Assert.Equal(0, bound.CycleStops);
        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Fact]
    public void DepthCap_ChainNeedingCapPlusOneFrames_StopsAtTheCapWithTheSentinel()
    {
        // One frame more than the cap admits: the deepest ask is refused, the
        // sentinel propagates up the chain, and the terminal call on the last
        // temporary is reported as unknown — the pass completes either way.
        const int links = 8;
        var (bag, bound) = Enforce(
            Chain(links), cap: links * FramesPerFlatLink - 1);

        Assert.Equal(links * FramesPerFlatLink - 1, bound.MaxObservedDepth);
        Assert.True(bound.DepthCapStops >= 1, $"expected a cap stop, got {bound.DepthCapStops}");
        Assert.Equal(0, bound.CycleStops);
        // The refused frame belongs to the root link — the deepest one reached.
        Assert.All(bound.StoppedNames, name => Assert.Equal(Link(1), name));
        Assert.Contains(bag, d => d.Code == DiagnosticCode.UnknownExternalCall
            && d.Message.Contains($"{Link(links)}.ToList", StringComparison.Ordinal));
        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.AnalysisICE);
    }

    [Fact]
    public void DepthCap_OfTwo_AdmitsExactlyOneLink()
    {
        // The smallest useful cap: one link (ask + structural level) resolves;
        // a second link needs a third frame and is refused.
        var source = """
            §M{m001:CapTwo}
              §F{f001:Run:priv} (List<i32>:items) -> void
                §B{_chainWhere001} §C{items.Where} §/C
                §C{_chainWhere001.ToList} §/C
                §B{_chainWhere002} §C{_chainWhere001.Where} §/C
                §C{_chainWhere002.ToList} §/C
            """;

        var (bag, bound) = Enforce(source, cap: FramesPerFlatLink);

        Assert.Equal(FramesPerFlatLink, bound.MaxObservedDepth);
        Assert.True(bound.DepthCapStops >= 1);
        Assert.All(bound.StoppedNames, name => Assert.Equal("_chainWhere001", name));
        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.UnknownExternalCall
            && d.Message.Contains("_chainWhere001.ToList", StringComparison.Ordinal));
        Assert.Contains(bag, d => d.Code == DiagnosticCode.UnknownExternalCall
            && d.Message.Contains("_chainWhere002.ToList", StringComparison.Ordinal));
    }

    [Fact]
    public void DefaultDepthCap_IsWellPastTheCorpus_AndStillBackstopsAnAbsurdChain()
    {
        // Pins the default's value class: the corpus needs 13 frames, so the
        // default must sit an order of magnitude above that — and a chain past
        // it must complete rather than exhaust the stack.
        const int defaultCap = EffectEnforcementPass.AstResolutionBound.DefaultDepthCap;
        Assert.True(defaultCap >= 130,
            $"default cap {defaultCap} is not an order of magnitude past the corpus's 13 frames");

        // Exactly at the default: admitted.
        const int atCap = defaultCap / FramesPerFlatLink;
        var (under, underBound) = Enforce(Chain(atCap));
        Assert.Equal(0, underBound.DepthCapStops);
        Assert.Equal(defaultCap, underBound.MaxObservedDepth);
        Assert.DoesNotContain(under, d => d.Code == DiagnosticCode.UnknownExternalCall);

        // One link past it: refused at the root, and the pass still completes.
        var (over, overBound) = Enforce(Chain(atCap + 1));
        Assert.True(overBound.DepthCapStops >= 1);
        Assert.Equal(defaultCap, overBound.MaxObservedDepth);
        Assert.Contains(over, d => d.Code == DiagnosticCode.UnknownExternalCall
            && d.Message.Contains($"{Link(atCap + 1)}.ToList", StringComparison.Ordinal));
    }

    [Fact]
    public void Bound_IsPerPassInstance_AndReadsZeroWhenNothingWasAsked()
    {
        // Review round 2, m4: the counters are per PASS INSTANCE, not per
        // Enforce call — nothing resets them, and a caller that enforces twice
        // with one pass sees the sum. Pinned so the doc and the name agree.
        var (_, bound) = Enforce("""
            §M{m001:Empty}
              §F{f001:Run:priv} () -> void
                §P "hello"
            """);

        Assert.Equal(0, bound.CycleStops);
        Assert.Equal(0, bound.DepthCapStops);
        Assert.Equal(0, bound.MaxObservedDepth);
        Assert.Empty(bound.StoppedNames);
    }

    [Fact]
    public void Bound_AccumulatesAcrossTwoEnforceCallsOnOnePass()
    {
        // The other half of m4: two Enforce calls on ONE pass accumulate.
        var bound = new EffectEnforcementPass.AstResolutionBound();
        var pass = new EffectEnforcementPass(new DiagnosticBag()) { AstResolution = bound };
        var source = """
            §M{m001:SelfCycle}
              §F{f001:Loop:priv} (FailureAwareBatchScheduler:scheduler) -> void
                §B{~x} §C{scheduler.MarkFailure} §A x §/C
            """;

        pass.Enforce(Parse(source));
        var afterFirst = bound.CycleStops;
        pass.Enforce(Parse(source));

        Assert.True(afterFirst >= 1);
        Assert.Equal(afterFirst * 2, bound.CycleStops);
    }

    // ===== helpers =====

    /// <summary>
    /// The converter's chain shape from the issue: <c>_chainWhere001 =
    /// items.Where()</c>, <c>_chainWhere002 = _chainWhere001.Where()</c>, …,
    /// then <c>_chainWhere{links}.ToList()</c>.
    ///
    /// <para><b>The names matter, in every probe in this file.</b>
    /// <c>CallGraphAnalysis.Build</c> runs the binder internally even when no
    /// binder ran in front of the pass, and a binder-typed receiver answers
    /// from the bound tree WITHOUT an AST ask — such a probe would silently
    /// measure nothing. A converter-synthesized name
    /// (<c>_{prefix}{Hint}{NNN}</c>, per <c>Binder.IsConverterSynthesizedName</c>)
    /// is left UNREPORTED-unresolved by the binder, which is exactly the shape
    /// that falls through to the AST search this file bounds — and the
    /// <c>_chainNNN</c> shape the issue names. Any new probe here must use
    /// converter-shaped names for the same reason.</para>
    ///
    /// <para>The parameter <c>items</c> is binder-typed, so the chain's root
    /// costs no ask: resolving <c>_chainWhere{links}</c> costs exactly
    /// <c>links × <see cref="FramesPerFlatLink"/></c> frames, and the terminal
    /// <c>.ToList</c> is a KNOWN call only if the whole chain resolved.</para>
    /// </summary>
    private static string Chain(int links)
    {
        var sb = new StringBuilder();
        sb.AppendLine("§M{m001:Chain}");
        sb.AppendLine("  §F{f001:Run:priv} (List<i32>:items) -> void");
        for (var i = 1; i <= links; i++)
            sb.AppendLine($"    §B{{{Link(i)}}} §C{{{(i == 1 ? "items" : Link(i - 1))}.Where}} §/C");
        sb.AppendLine($"    §C{{{Link(links)}.ToList}} §/C");
        return sb.ToString();
    }

    /// <summary>
    /// The C1 shape: the same chain, but each link one <c>§IF</c> deeper than
    /// the last, so the structural walk costs a frame per level and the total
    /// grows quadratically (30 links → 525 frames, 50 → 1375) while the number
    /// of name asks grows linearly. An ask-counting bound cannot see this.
    /// </summary>
    private static string NestedChain(int links)
    {
        var sb = new StringBuilder();
        sb.AppendLine("§M{m001:Nested}");
        sb.AppendLine("  §F{f001:Run:priv} (List<i32>:items, bool:flag) -> void");
        for (var i = 1; i <= links; i++)
        {
            var indent = new string(' ', 4 + ((i - 1) * 2));
            sb.AppendLine($"{indent}§IF{{if{i:000}}} flag");
            sb.AppendLine(
                $"{indent}  §B{{{Link(i)}}} §C{{{(i == 1 ? "items" : Link(i - 1))}.Where}} §/C");
        }
        sb.AppendLine($"{new string(' ', 4 + (links * 2))}§C{{{Link(links)}.ToList}} §/C");
        return sb.ToString();
    }

    private static string Link(int i) => $"_chainWhere{i:000}";

    private static ModuleNode Parse(string source)
    {
        var bag = new DiagnosticBag();
        var module = new Parser(new Lexer(source, bag).TokenizeAllForParser(), bag).Parse();
        Assert.False(bag.HasErrors,
            "fixture must parse: " + string.Join("; ", bag.Errors.Select(e => $"{e.Code} {e.Message}")));
        return module;
    }

    private static (DiagnosticBag Bag, EffectEnforcementPass.AstResolutionBound Bound) Enforce(
        string source,
        int? cap = null)
    {
        var module = Parse(source);
        var bag = new DiagnosticBag();
        var bound = cap is { } c
            ? new EffectEnforcementPass.AstResolutionBound { DepthCap = c }
            : new EffectEnforcementPass.AstResolutionBound();
        // No binder in front of the pass — the reproduction path.
        new EffectEnforcementPass(bag) { AstResolution = bound }.Enforce(module);
        return (bag, bound);
    }
}
