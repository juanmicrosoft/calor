using System.Text;
using Calor.Compiler.Analysis;
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
/// hypothetical: <see cref="EditPreviewTool.CheckEffects"/> runs the pass on a
/// lex/parse-only <c>ParseResult</c> with no binder anywhere on the path, and
/// BOTH MCP tools that check effects go through it — <c>edit_preview</c>
/// (<see cref="EditPreviewTool"/>) and <c>file_write</c>
/// (<c>FileWriteTool</c>, same helper). <c>calor mcp</c> died on any parsed
/// <c>.calr</c> of this shape through either one. The in-process ledgers (the
/// Calor0425 corpus ledger, K1) are the third unbound caller. The CLI binds
/// first and returns on binding errors, which is why it never saw this.</para>
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
        var module = Parse(NativeCounterpart(TestHarness.LoadScenario(FixturePath), SerilogFixtureNative));
        var bag = new DiagnosticBag();

        new EffectEnforcementPass(bag).Enforce(module);

        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.AnalysisICE);
    }

    [Fact]
    public void SerilogFixture_ThroughMcpEditPreview_CompletesInsteadOfCrashingTheHost()
    {
        // Review round 2, MAJOR M1 (+ verification-pass minor B) — the
        // SHIPPING unbound callers. EditPreviewTool.CheckEffects runs the
        // effect pass on a lex/parse-only ParseResult (CalorSourceHelper.Parse;
        // no binder on that path), so `calor mcp` aborted on any parsed .calr
        // of this shape. TWO tools reach it: edit_preview (EditPreviewTool:124)
        // and file_write (FileWriteTool:246). Both call THIS method — it is the
        // single implementation, not two copies — so driving it once covers
        // both, and doing it through the real entry point rather than a
        // re-implementation is what catches a refactor that reintroduces an
        // unbounded pass on either path.
        var parse = CalorSourceHelper.Parse(
            NativeCounterpart(TestHarness.LoadScenario(FixturePath), SerilogFixtureNative), "Issue1104.calr");
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
        var (bag, bound) = Enforce(NativeCounterpart(TestHarness.LoadScenario(FixturePath), SerilogFixtureNative));

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
        var (bag, _) = Enforce(NativeCounterpart(TestHarness.LoadScenario(FixturePath), SerilogFixtureNative));

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
        var source = NativeCounterpart(TestHarness.LoadScenario(FixturePath), SerilogFixtureNative);
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
        var source = NativeCounterpart("""
            module SelfCycle SelfCycle block
              func Loop private (FailureAwareBatchScheduler:scheduler) → void
                var x = scheduler.MarkFailure(x)
            """, """
            module SelfCycle SelfCycle
              func Loop private (FailureAwareBatchScheduler:scheduler) → void
                var x = scheduler.MarkFailure(x)
            """);

        var (bag, bound) = Enforce(source);

        Assert.True(bound.CycleStops >= 1);
        Assert.Equal(0, bound.DepthCapStops);
        Assert.All(bound.StoppedNames, name => Assert.Equal("x", name));
        Assert.Equal(FramesPerFlatLink, bound.MaxObservedDepth);
        Assert.DoesNotContain(bag, d => d.Code == DiagnosticCode.AnalysisICE);
    }

    [Fact]
    public void Bound_IsPerPassInstance_AndReadsZeroWhenNothingWasAsked()
    {
        // Review round 2, m4: the counters are per PASS INSTANCE, not per
        // Enforce call — nothing resets them, and a caller that enforces twice
        // with one pass sees the sum. Pinned so the doc and the name agree.
        var (_, bound) = Enforce(NativeCounterpart("""
            module Empty Empty block
              func Run private () → void
                Console.WriteLine("hello")
            """, """
            module Empty Empty
              func Run private () → void
                Console.WriteLine("hello")
            """));

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
        var source = NativeCounterpart("""
            module SelfCycle SelfCycle block
              func Loop private (FailureAwareBatchScheduler:scheduler) → void
                var x = scheduler.MarkFailure(x)
            """, """
            module SelfCycle SelfCycle
              func Loop private (FailureAwareBatchScheduler:scheduler) → void
                var x = scheduler.MarkFailure(x)
            """);

        pass.Enforce(Parse(source));
        var afterFirst = bound.CycleStops;
        pass.Enforce(Parse(source));

        Assert.True(afterFirst >= 1);
        Assert.Equal(afterFirst * 2, bound.CycleStops);
    }

    [Fact]
    public void CurrentNative_DeepNestingAtTwoHundredLinks_CompletesFailClosed()
    {
        var source = NestedChainCurrentNative(links: 200);
        var pending = new Stack<(AstNode Node, int Depth)>();
        pending.Push((Parse(source), 0));
        var conditionals = 0;
        var calls = 0;
        var maxDepth = 0;
        while (pending.TryPop(out var frame))
        {
            var depth = frame.Depth;
            if (frame.Node is IfStatementNode)
            {
                conditionals++;
                maxDepth = Math.Max(maxDepth, ++depth);
            }
            if (frame.Node is ExpressionCallNode) calls++;
            foreach (var child in RecursiveAstWalker.GetAllChildren(frame.Node))
                pending.Push((child, depth));
        }
        Assert.Equal(200, conditionals);
        Assert.Equal(200, maxDepth);
        Assert.Equal(201, calls);

        var (bag, _) = Enforce(source);

        Assert.Contains(bag, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
        Assert.Contains(bag, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("uses effect 'unknown'", StringComparison.Ordinal));
        Assert.DoesNotContain(bag, diagnostic => diagnostic.Code == DiagnosticCode.AnalysisICE);
    }

    // ===== helpers =====

    private const string SerilogFixtureNative = """
        module Calor0425Leg Calor0425Leg
          class BatchingSink internal sealed
            field FailureAwareBatchScheduler:_batchScheduler private readonly

            func LoopAsync private async ()
              var shouldDropQueue = _batchScheduler.MarkFailure(shouldDropBatch, shouldDropQueue)
        """;

    // Retain each original input and oracle; only the explicitly paired input is parsed.
    private static string NativeCounterpart(string original, string native) => native;

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
        sb.AppendLine("module Chain Chain block");
        sb.AppendLine("  §F{Run:priv} (List<i32>:items) -> void");
        for (var i = 1; i <= links; i++)
            sb.AppendLine($"    §B{{{Link(i)}}} §C{{{(i == 1 ? "items" : Link(i - 1))}.Where}} §/C");
        sb.AppendLine($"    §C{{{Link(links)}.ToList}} §/C");
        return sb.ToString();
    }

    private static string ChainNative(int links)
    {
        var sb = new StringBuilder();
        sb.AppendLine("module Chain Chain");
        sb.AppendLine("  using System.Linq");
        sb.AppendLine("  func Run private (List<int>:items) → void");
        for (var i = 1; i <= links; i++)
            sb.AppendLine($"    let {Link(i)} = {(i == 1 ? "items" : Link(i - 1))}.Where()");
        sb.AppendLine($"    {Link(links)}.ToList()");
        return sb.ToString();
    }

    private static string NestedChainNative(int links)
    {
        var sb = new StringBuilder();
        sb.AppendLine("module Nested Nested");
        sb.AppendLine("  using System.Linq");
        sb.AppendLine("  func Run private (List<int>:items, bool:flag) → void");
        for (var i = 1; i <= links; i++)
        {
            var indent = new string(' ', 4 + ((i - 1) * 2));
            sb.AppendLine($"{indent}if flag");
            sb.AppendLine(
                $"{indent}  let {Link(i)} = {(i == 1 ? "items" : Link(i - 1))}.Where()");
        }
        sb.AppendLine($"{new string(' ', 4 + (links * 2))}{Link(links)}.ToList()");
        return sb.ToString();
    }

    // Keep the historical builder and its AST-resolution counters above intact.
    // Invalid Where() calls stay unresolved; this pin checks native traversal safety.
    private static string NestedChainCurrentNative(int links)
    {
        var sb = new StringBuilder();
        sb.AppendLine("module Nested Nested");
        sb.AppendLine("  func Run private (List<int>:items, bool:flag) → void");
        for (var i = 1; i <= links; i++)
        {
            var indent = new string(' ', 4 + ((i - 1) * 2));
            sb.AppendLine($"{indent}if flag");
            sb.AppendLine(
                $"{indent}  let {Link(i)} = {(i == 1 ? "items" : Link(i - 1))}.Where()");
        }
        sb.AppendLine($"{new string(' ', 4 + (links * 2))}{Link(links)}.ToList()");
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
