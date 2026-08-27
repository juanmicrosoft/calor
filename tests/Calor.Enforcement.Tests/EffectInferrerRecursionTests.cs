using System.Text;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Enforcement.Tests;

/// <summary>
/// #1104 (v0.16 MUST W3(c)) — the nested <c>EffectInferrer</c>'s AST-side
/// local-type resolution is BOUNDED: a visited set keyed on the name being
/// resolved, plus a depth cap as a backstop (<see cref="EffectEnforcementPass.AstResolutionBound"/>).
/// A cycle resolves to the unknown-type sentinel instead of recursing.
/// </summary>
/// <remarks>
/// <para>Every test here enforces the PARSED module directly —
/// <c>new EffectEnforcementPass(bag).Enforce(module)</c> with no binder in
/// front of it. That is the reproduction path from the issue: the CLI binds
/// first and returns on binding errors, so the modules that cycle never reach
/// the pass there; an in-process caller (the Calor0425 corpus ledger, K1) does
/// reach it.</para>
///
/// <para><b>Discriminating revert</b> (roadmap §3.1 W3(c), verbatim): "revert
/// (c) → the recursion test crashes the host". A StackOverflowException is a
/// fail-fast — no catch observes it — so the crash-repro pins in this file do
/// not go red, they take the test host down. Verified on the unfixed tree for
/// the committed fixture and for the two synthetic cycle shapes (PR body records
/// the runs).</para>
///
/// <para><b>Gate 6 / §9 (no resolution answer changes on acyclic code)</b>: the
/// visited set fires only when a name is asked for while its own resolution is
/// on the stack, which is a cycle by construction; the cap is an order of
/// magnitude past what the corpus reaches (max observed nesting 6 over the 364
/// corpus modules, default cap <see cref="EffectEnforcementPass.AstResolutionBound.DefaultDepthCap"/>).
/// The long-chain tests below pin that ordinary deep-but-acyclic code resolves
/// all the way down; the frozen corpus ledgers pin it over the corpus.</para>
/// </remarks>
public class EffectInferrerRecursionTests
{
    private const string FixturePath = "Effects/Issue1104_BatchingSink_LoopAsync.calr";

    // ===== crash-repro pin over the committed Serilog-reduced fixture =====

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
    public void SerilogFixture_CycleResolvesToSentinel_ByVisitedSetNotByCap()
    {
        var (bag, bound) = Enforce(TestHarness.LoadScenario(FixturePath));

        // The hoisted `out var` binding is the name whose own resolution was
        // re-entered; it resolved by the VISITED SET, never by the backstop.
        Assert.True(bound.CycleStops >= 1, $"expected a cycle stop, got {bound.CycleStops}");
        Assert.Equal(0, bound.DepthCapStops);
        Assert.All(bound.StoppedNames, name => Assert.Equal("shouldDropQueue", name));
        // One re-entry deep: shouldDropQueue → (argument) shouldDropQueue.
        Assert.Equal(2, bound.MaxObservedDepth);
        // The cycle is a converted-code artifact, not an internal error; the
        // call it sits on is reported the ordinary way (unknown external call).
        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.AnalysisICE);
        Assert.Contains(bag, d => d.Code == DiagnosticCode.UnknownExternalCall
            && d.Message.Contains("_batchScheduler.MarkFailure", StringComparison.Ordinal));
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
        Assert.Equal(1, bound.MaxObservedDepth);
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
        // a → b → a: the re-entry is two asks deep.
        Assert.Equal(2, bound.MaxObservedDepth);
        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.AnalysisICE);
    }

    [Fact]
    public void ThreeNodeCycle_InsideNestedBlocks_ResolvesToSentinel()
    {
        // The search descends into nested statement bodies; a cycle spread
        // across them is still one cycle.
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
        Assert.Equal(3, bound.MaxObservedDepth);
        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.AnalysisICE);
    }

    // ===== acyclic code is untouched =====

    [Fact]
    public void LongAcyclicChain_ResolvesAllTheWayDown_NoStopOfEitherKind()
    {
        // 40 chained LINQ temporaries, each typed from the previous one. This
        // is deeper than anything in the corpus (max 6) and must still resolve
        // normally: the terminal `.ToList` on the last temporary is a KNOWN
        // call (no Calor0411), and neither guard fired. Pins that the bound is
        // not "give up at depth N" on ordinary code.
        const int links = 40;
        var (bag, bound) = Enforce(Chain(links));

        Assert.Equal(0, bound.CycleStops);
        Assert.Equal(0, bound.DepthCapStops);
        // Walked to its root: one ask per link, none refused.
        Assert.Equal(links, bound.MaxObservedDepth);
        Assert.True(bound.MaxObservedDepth < EffectEnforcementPass.AstResolutionBound.DefaultDepthCap);
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
        Assert.Equal(1, bound.MaxObservedDepth);
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
        Assert.Equal(2, bound.MaxObservedDepth);
        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.UnknownExternalCall
            && d.Message.Contains("_chainWhere002.ToList", StringComparison.Ordinal));
    }

    // ===== the depth cap: boundary, backstop, default =====

    [Fact]
    public void DepthCap_ChainNeedingExactlyCapAsks_Resolves()
    {
        // Resolving the last link nests one ask per link. With the cap set to
        // exactly the link count the deepest ask (the root, _chainWhere001) is
        // admitted and the terminal call is known.
        const int cap = 8;
        var (bag, bound) = Enforce(Chain(links: cap), cap);

        Assert.Equal(cap, bound.MaxObservedDepth);
        Assert.Equal(0, bound.DepthCapStops);
        Assert.Equal(0, bound.CycleStops);
        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Fact]
    public void DepthCap_ChainNeedingCapPlusOneAsks_StopsAtTheCapWithTheSentinel()
    {
        // One link longer than the cap admits: the root ask is refused, the
        // sentinel propagates up the chain, and the terminal call on the last
        // temporary is reported as unknown — the pass completes either way.
        const int cap = 8;
        var (bag, bound) = Enforce(Chain(links: cap + 1), cap);

        Assert.Equal(cap, bound.MaxObservedDepth);
        Assert.True(bound.DepthCapStops >= 1, $"expected a cap stop, got {bound.DepthCapStops}");
        Assert.Equal(0, bound.CycleStops);
        // The refused ask is the root link — the (cap + 1)-th nested one.
        Assert.All(bound.StoppedNames, name => Assert.Equal(Link(1), name));
        Assert.Contains(bag, d => d.Code == DiagnosticCode.UnknownExternalCall
            && d.Message.Contains($"{Link(cap + 1)}.ToList", StringComparison.Ordinal));
        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.AnalysisICE);
    }

    [Fact]
    public void DepthCap_OfOne_StillAdmitsADirectAsk()
    {
        // The smallest cap: a one-link chain is one ask deep and resolves;
        // a second link nests one more and is refused.
        var source = """
            §M{m001:CapOne}
              §F{f001:Run:priv} (List<i32>:items) -> void
                §B{_chainWhere001} §C{items.Where} §/C
                §C{_chainWhere001.ToList} §/C
                §B{_chainWhere002} §C{_chainWhere001.Where} §/C
                §C{_chainWhere002.ToList} §/C
            """;

        var (bag, bound) = Enforce(source, cap: 1);

        Assert.Equal(1, bound.MaxObservedDepth);
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
        // Pins the default's value class: the corpus never nests past 6, so
        // the default must sit an order of magnitude above that — and a chain
        // past it must complete rather than exhaust the stack.
        const int defaultCap = EffectEnforcementPass.AstResolutionBound.DefaultDepthCap;
        Assert.True(defaultCap >= 60, $"default cap {defaultCap} is too close to the corpus maximum of 6");

        // Exactly at the default: admitted.
        var (under, underBound) = Enforce(Chain(links: defaultCap));
        Assert.Equal(0, underBound.DepthCapStops);
        Assert.Equal(defaultCap, underBound.MaxObservedDepth);
        Assert.DoesNotContain(under, d => d.Code == DiagnosticCode.UnknownExternalCall);

        // Past it: refused at the root, and the pass still completes.
        var (over, overBound) = Enforce(Chain(links: defaultCap + 8));
        Assert.True(overBound.DepthCapStops >= 1);
        Assert.Equal(defaultCap, overBound.MaxObservedDepth);
        Assert.Contains(over, d => d.Code == DiagnosticCode.UnknownExternalCall
            && d.Message.Contains($"{Link(defaultCap + 8)}.ToList", StringComparison.Ordinal));
    }

    [Fact]
    public void Bound_IsPerEnforce_AndReadsZeroWhenNothingWasAsked()
    {
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

    // ===== helpers =====

    /// <summary>
    /// The converter's chain shape from the issue: <c>_chainWhere001 =
    /// items.Where()</c>, <c>_chainWhere002 = _chainWhere001.Where()</c>, …,
    /// then <c>_chainWhere{links}.ToList()</c>.
    ///
    /// <para>The names matter. <c>CallGraphAnalysis.Build</c> runs the binder
    /// internally even when no binder ran in front of the pass, and a
    /// binder-typed receiver answers from the bound tree without an AST ask.
    /// A converter-synthesized name (<c>_{prefix}{Hint}{NNN}</c>) is left
    /// UNREPORTED-unresolved by the binder, which is exactly the shape that
    /// falls through to the AST search this file bounds. The parameter
    /// <c>items</c> is binder-typed, so the chain's root costs no ask:
    /// resolving <c>_chainWhere{links}</c> nests exactly <c>links</c> asks, and
    /// the terminal <c>.ToList</c> is a KNOWN call only if the whole chain
    /// resolved to a LINQ result type.</para>
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
