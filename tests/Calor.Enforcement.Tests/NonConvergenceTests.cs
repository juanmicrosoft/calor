using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Enforcement.Tests;

/// <summary>
/// v0.16 W5 — "Silent stop made loud, in the effects band" (roadmap-v0.16.md §3.1
/// W5, gate 11, §6 rows "ProcessScc emits Calor0600" / "PropagateInstantiatedCharges
/// cap silent").
///
/// <para>Two loops in <see cref="EffectEnforcementPass"/> have an iteration cap:
/// the SCC fixpoint in <c>ProcessScc</c> (default 100 rounds, floored at the
/// SCC's size + 1) and the instantiated-charge worklist in
/// <c>PropagateInstantiatedCharges</c> (default 10 000 steps). Before 0.16 the
/// first reported the API-strictness code Calor0600 as a warning and the second
/// said nothing. Both now report <b>Calor0406 <c>EffectInferenceDidNotConverge</c></b>
/// as an error. The caps are injectable so these pins drive three-hop fixtures
/// into the cap at 2 instead of building a hundred-function SCC.</para>
///
/// <para>Gate 11's discriminating pins are
/// <see cref="ProcessScc_CapHit_Calor0406_IsReported"/> and
/// <see cref="PropagateInstantiatedCharges_CapHit_Calor0406_IsReported"/>:
/// revert either emission and its pin is red.</para>
/// </summary>
public class NonConvergenceTests
{
    // ------------------------------------------------------------------
    // Fixtures
    // ------------------------------------------------------------------

    /// <summary>
    /// A → B → C → A, each member contributing a DISTINCT effect, so the SCC
    /// fixpoint needs more than one growing round: a round carries an effect
    /// along a forward edge at once but along a back edge only one hop, so the
    /// ring converges in three rounds — two that grow and one that confirms —
    /// whichever order Tarjan hands the members back in. Every member declares
    /// the full union so the converged program is clean and the only thing a cap
    /// can add is Calor0406.
    /// </summary>
    private const string ThreeHopMutualRecursion = """
        §M{m001:M}
          §F{f001:A:pub} (i32:n) -> i32
            §E{cw,cr,fs:r}
            §C{Console.WriteLine} §A "a" §/C
            §IF{if1} (> n 0)
              §R §C{B} §A (- n 1) §/C
            §R n
          §F{f002:B:pub} (i32:n) -> i32
            §E{cw,cr,fs:r}
            §B{s:str} §C{Console.ReadLine}
            §IF{if2} (> n 0)
              §R §C{C} §A (- n 1) §/C
            §R n
          §F{f003:C:pub} (i32:n) -> i32
            §E{cw,cr,fs:r}
            §B{t:str} §C{File.ReadAllText} §A "x" §/C
            §IF{if3} (> n 0)
              §R §C{A} §A (- n 1) §/C
            §R n
        """;

    /// <summary>
    /// The E3b three-hop rank-1 chain (PR #1106,
    /// <c>StrictnessBatchTests.GenericInstantiation_ChargePropagatesThroughThreeHops</c>):
    /// <c>Run&lt;eff e&gt;</c> is instantiated at <c>cw</c> inside <c>Outer</c>, and the
    /// charge must travel <c>Outer → Top → Top2</c>. Only <c>Outer</c> seeds the
    /// worklist, so the propagation takes exactly three dequeues: <c>Outer</c>
    /// (charging <c>Top</c>), <c>Top</c> (charging <c>Top2</c>), <c>Top2</c> (no
    /// callers). <c>Top2</c> under-declares, so whether it was charged is
    /// observable as its Calor0410.
    /// </summary>
    private const string ThreeHopRank1Chain = """
        §M{m001:M}
          §F{f001:Run:pub}<eff e> (Func<i32>:g §E{e}) -> i32
            §E{e}
            §R §C{g}
          §F{f002:Outer:pub} (Func<i32>:h §E{cw}) -> i32
            §E{cw}
            §R §C{Run} §A h §/C
          §F{f003:Top:pub} (Func<i32>:q §E{cw}) -> i32
            §E{cw}
            §R §C{Outer} §A q §/C
          §F{f004:Top2:pub} (Func<i32>:r §E{cw}) -> i32
            §E{}
            §R §C{Top} §A r §/C
        """;

    /// <summary>
    /// One hop deeper — <c>Outer → Top → Top2 → Top3</c> — so a cap of 2 leaves
    /// <c>Top3</c> UNCHARGED (the dequeue of <c>Top2</c>, which would charge it,
    /// never happens) and the silent hole W5 closes is observable as
    /// <c>Top3</c>'s missing Calor0410.
    /// </summary>
    private const string FourHopRank1Chain = """
        §M{m001:M}
          §F{f001:Run:pub}<eff e> (Func<i32>:g §E{e}) -> i32
            §E{e}
            §R §C{g}
          §F{f002:Outer:pub} (Func<i32>:h §E{cw}) -> i32
            §E{cw}
            §R §C{Run} §A h §/C
          §F{f003:Top:pub} (Func<i32>:q §E{cw}) -> i32
            §E{cw}
            §R §C{Outer} §A q §/C
          §F{f004:Top2:pub} (Func<i32>:r §E{cw}) -> i32
            §E{cw}
            §R §C{Top} §A r §/C
          §F{f005:Top3:pub} (Func<i32>:s §E{cw}) -> i32
            §E{}
            §R §C{Top2} §A s §/C
        """;

    private const string SelfRecursive = """
        §M{m001:M}
          §F{f001:Fact:pub} (i32:n) -> i32
            §E{cw}
            §C{Console.WriteLine} §A "step" §/C
            §IF{if1} (<= n 1)
              §R 1
            §R (* n §C{Fact} §A (- n 1) §/C)
        """;

    private const string NoRecursionNoRows = """
        §M{m001:M}
          §F{f001:Leaf:pub} () -> void
            §E{cw}
            §C{Console.WriteLine} §A "leaf" §/C
          §F{f002:Root:pub} () -> void
            §E{cw}
            §C{Leaf} §/C
        """;

    /// <summary>
    /// A ring of <paramref name="n"/> functions, <c>F1 → F2 → … → Fn → F1</c>,
    /// each declaring <c>cw</c> and one of them printing. One SCC of n members
    /// sharing one effect: every member changes in round 1, so a cap of 1 fires
    /// and names all n.
    /// </summary>
    private static string Ring(int n)
    {
        var sb = new StringBuilder("§M{m001:M}\n");
        for (var i = 1; i <= n; i++)
        {
            var next = i == n ? 1 : i + 1;
            sb.Append($"  §F{{f{i:D4}:F{i}:pub}} (i32:n) -> i32\n    §E{{cw}}\n");
            if (i == 1)
                sb.Append("    §C{Console.WriteLine} §A \"x\" §/C\n");
            sb.Append($"    §IF{{if{i}}} (> n 0)\n      §R §C{{F{next}}} §A (- n 1) §/C\n    §R n\n");
        }
        return sb.ToString();
    }

    /// <summary>
    /// The reviewer's counter-example to "a >100-member ring of distinct effects":
    /// a doubly-linked chain <c>A1 ⇄ A2 ⇄ … ⇄ An</c> sharing ONE effect (only
    /// <c>A1</c> prints). Under Tarjan's pop order the effect travels the chain
    /// one back edge per round, so the SCC needs about n rounds — n = 101 hits a
    /// flat cap of 100 (measured), which is why the effective default cap is
    /// floored at the SCC's size + 1.
    /// </summary>
    private static string DoubleChain(int n)
    {
        var sb = new StringBuilder("§M{m001:M}\n");
        for (var i = 1; i <= n; i++)
        {
            sb.Append($"  §F{{f{i:D4}:A{i}:pub}} (i32:n) -> i32\n    §E{{cw}}\n");
            if (i == 1)
                sb.Append("    §C{Console.WriteLine} §A \"x\" §/C\n");
            sb.Append($"    §IF{{if{i}}} (> n 0)\n");
            if (i < n)
                sb.Append($"      §B{{u{i}:i32}} §C{{A{i + 1}}} §A (- n 1) §/C\n");
            sb.Append(i > 1 ? $"      §R §C{{A{i - 1}}} §A (- n 1) §/C\n" : "      §R 0\n");
            sb.Append("    §R n\n");
        }
        return sb.ToString();
    }

    private static (ModuleNode Module, DiagnosticBag Diagnostics) Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(
            new Lexer(source, diagnostics).TokenizeAllForParser(),
            diagnostics).Parse();
        Assert.False(diagnostics.HasErrors,
            "fixture must parse: " + string.Join("; ", diagnostics.Errors.Select(d => d.Message)));
        return (module, diagnostics);
    }

    /// <summary>
    /// Runs the pass directly with injected caps. <c>null</c> injects nothing for
    /// that cap, so the negative pins exercise the same constructor path the
    /// compiler driver (<c>Program.cs</c>) uses — including the size-floored
    /// effective SCC cap, which only applies when nothing is injected.
    /// </summary>
    private static DiagnosticBag Enforce(
        string source,
        int? sccCap = null,
        int? chargeCap = null,
        UnknownCallPolicy policy = UnknownCallPolicy.Strict)
    {
        var (module, diagnostics) = Parse(source);
        var pass = (sccCap, chargeCap) switch
        {
            (null, null) => new EffectEnforcementPass(diagnostics, policy),
            (not null, null) => new EffectEnforcementPass(diagnostics, policy) { SccFixpointIterationCap = sccCap.Value },
            (null, not null) => new EffectEnforcementPass(diagnostics, policy) { InstantiatedChargeIterationCap = chargeCap.Value },
            _ => new EffectEnforcementPass(diagnostics, policy)
            {
                SccFixpointIterationCap = sccCap!.Value,
                InstantiatedChargeIterationCap = chargeCap!.Value,
            },
        };
        pass.Enforce(module);
        return diagnostics;
    }

    private static IReadOnlyList<Diagnostic> NonConvergence(DiagnosticBag diagnostics) =>
        diagnostics.Where(d => d.Code == DiagnosticCode.EffectInferenceDidNotConverge).ToList();

    // ------------------------------------------------------------------
    // Gate 11 pin 1 — ProcessScc
    // ------------------------------------------------------------------

    /// <summary>
    /// Gate 11, denominator 1 (<c>ProcessScc</c>). Discriminating revert: delete
    /// the <c>if (changed) ReportDidNotConverge(...)</c> after the fixpoint loop
    /// and this is red.
    /// </summary>
    [Fact]
    public void ProcessScc_CapHit_Calor0406_IsReported()
    {
        var diagnostics = Enforce(ThreeHopMutualRecursion, sccCap: 2);

        var reported = Assert.Single(NonConvergence(diagnostics));
        Assert.Equal(DiagnosticSeverity.Error, reported.Severity);
        Assert.Contains(reported, diagnostics.Errors);

        // (e) the message names the site, the cap, and the functions involved.
        Assert.Contains("SCC fixpoint", reported.Message);
        Assert.Contains("cap of 2 rounds", reported.Message);
        Assert.Contains("'A'", reported.Message);
        Assert.Contains("'B'", reported.Message);
        Assert.Contains("'C'", reported.Message);
        Assert.DoesNotContain("instantiated-charge", reported.Message);
    }

    [Fact]
    public void ProcessScc_CapHit_IsReportedAtTheDeclarationOfAnSccMember()
    {
        var (module, diagnostics) = Parse(ThreeHopMutualRecursion);
        new EffectEnforcementPass(diagnostics) { SccFixpointIterationCap = 2 }.Enforce(module);

        var reported = Assert.Single(NonConvergence(diagnostics));
        var memberSpans = module.Functions.Select(f => f.Span).ToList();
        Assert.Contains(reported.Span, memberSpans);
    }

    /// <summary>
    /// (c) negative pin: the same fixture at the default cap converges — no
    /// Calor0406, and nothing else either, since every member declares the union.
    /// </summary>
    [Fact]
    public void ProcessScc_DefaultCap_Converges_NoCalor0406()
    {
        var diagnostics = Enforce(ThreeHopMutualRecursion);

        Assert.Empty(NonConvergence(diagnostics));
        Assert.Empty(diagnostics.Errors);
    }

    /// <summary>
    /// The three-hop ring converges in three rounds (two growing, one confirming)
    /// in both declaration orders, so the edge is 2 | 3: caps of 1 and 2 fire,
    /// 3 and above are silent. Pins that the cap is compared against the injected
    /// value and not a constant.
    /// </summary>
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    public void ProcessScc_CapBoundary(int cap, bool expectReport)
    {
        var diagnostics = Enforce(ThreeHopMutualRecursion, sccCap: cap);

        if (expectReport)
        {
            var reported = Assert.Single(NonConvergence(diagnostics));
            Assert.Contains($"cap of {cap} rounds", reported.Message);
        }
        else
        {
            Assert.Empty(NonConvergence(diagnostics));
        }
    }

    /// <summary>
    /// A directly self-recursive function is a singleton SCC with a self edge and
    /// goes through the same fixpoint loop (the #785-era fix). It needs two
    /// rounds — one to grow, one to confirm — so a cap of 1 fires and 2 does not.
    /// </summary>
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void ProcessScc_SelfRecursiveSingleton_HonoursTheCap(int cap, bool expectReport)
    {
        var diagnostics = Enforce(SelfRecursive, sccCap: cap);

        var reported = NonConvergence(diagnostics);
        if (expectReport)
        {
            Assert.Contains("'Fact'", Assert.Single(reported).Message);
        }
        else
        {
            Assert.Empty(reported);
            Assert.Empty(diagnostics.Errors);
        }
    }

    /// <summary>
    /// A non-recursive singleton never enters the fixpoint loop, so no cap — not
    /// even 1 — can make it report.
    /// </summary>
    [Fact]
    public void ProcessScc_NonRecursive_NeverReports_EvenAtCapOne()
    {
        var diagnostics = Enforce(NoRecursionNoRows, sccCap: 1, chargeCap: 1);

        Assert.Empty(NonConvergence(diagnostics));
        Assert.Empty(diagnostics.Errors);
    }

    /// <summary>
    /// The message lists at most eight names and then counts the rest: an
    /// eleven-member ring at cap 1 names eight and says "and 3 more". The same
    /// ring at the default cap converges.
    /// </summary>
    [Fact]
    public void ProcessScc_MessageTruncatesAfterEightNames()
    {
        var capped = Enforce(Ring(11), sccCap: 1);
        var reported = Assert.Single(NonConvergence(capped));
        Assert.Contains(", and 3 more", reported.Message);
        Assert.Equal(8, Regex.Matches(reported.Message, @"'F\d+'").Count);

        Assert.Empty(NonConvergence(Enforce(Ring(11))));
    }

    // ------------------------------------------------------------------
    // The size-floored default (review #2 M2)
    // ------------------------------------------------------------------

    /// <summary>
    /// The default SCC cap is not a flat 100: a doubly-linked chain of 101
    /// functions sharing one effect needs about 101 rounds under Tarjan's order
    /// (measured by the reviewer: 99 clean, 101 and 150 red at a flat 100), and
    /// a well-behaved program must never see Calor0406. With the floor at
    /// <c>scc.Count + 1</c> both sizes converge at the default.
    /// </summary>
    [Theory]
    [InlineData(101)]
    [InlineData(150)]
    public void DoubleChain_AtTheDefaultCap_Converges(int n)
    {
        var diagnostics = Enforce(DoubleChain(n));

        Assert.Empty(NonConvergence(diagnostics));
        Assert.Empty(diagnostics.Errors);
    }

    /// <summary>
    /// …and it is the floor that saves it: the same chain with a flat 100
    /// INJECTED is red, which also proves an injected cap is used verbatim
    /// (the pins above rely on that).
    /// </summary>
    [Fact]
    public void DoubleChain101_AtAnInjectedFlatCapOf100_DoesNotConverge()
    {
        var diagnostics = Enforce(DoubleChain(101), sccCap: 100);

        var reported = Assert.Single(NonConvergence(diagnostics));
        Assert.Contains("cap of 100 rounds", reported.Message);
        Assert.Contains(", and 93 more", reported.Message);
    }

    [Theory]
    [InlineData(1, 100)]
    [InlineData(3, 100)]
    [InlineData(99, 100)]
    [InlineData(100, 101)]
    [InlineData(150, 151)]
    public void EffectiveSccCap_WhenNothingIsInjected_IsTheDefaultFlooredAtSizePlusOne(int sccSize, int expected)
    {
        var pass = new EffectEnforcementPass(new DiagnosticBag());
        Assert.Equal(expected, pass.EffectiveSccFixpointIterationCap(sccSize));
    }

    [Theory]
    [InlineData(2, 3)]
    [InlineData(2, 150)]
    [InlineData(500, 150)]
    public void EffectiveSccCap_WhenInjected_IsTheInjectedValueVerbatim(int injected, int sccSize)
    {
        var pass = new EffectEnforcementPass(new DiagnosticBag()) { SccFixpointIterationCap = injected };
        Assert.Equal(injected, pass.EffectiveSccFixpointIterationCap(sccSize));
    }

    // ------------------------------------------------------------------
    // Gate 11 pin 2 — PropagateInstantiatedCharges
    // ------------------------------------------------------------------

    /// <summary>
    /// Gate 11, denominator 2 (<c>PropagateInstantiatedCharges</c>). Discriminating
    /// revert: delete the <c>if (queue.Count > 0) ReportDidNotConverge(...)</c>
    /// after the worklist and this is red.
    /// </summary>
    [Fact]
    public void PropagateInstantiatedCharges_CapHit_Calor0406_IsReported()
    {
        var diagnostics = Enforce(ThreeHopRank1Chain, chargeCap: 2);

        var reported = Assert.Single(NonConvergence(diagnostics));
        Assert.Equal(DiagnosticSeverity.Error, reported.Severity);
        Assert.Contains(reported, diagnostics.Errors);

        // (e) the message names the site, the cap, and the function still queued.
        Assert.Contains("instantiated-charge propagation", reported.Message);
        Assert.Contains("cap of 2 steps", reported.Message);
        Assert.Contains("'Top2'", reported.Message);
        Assert.DoesNotContain("SCC fixpoint", reported.Message);
    }

    /// <summary>
    /// What a cap hit MEANS, made observable: on the four-hop chain at cap 2 the
    /// dequeue that would charge <c>Top3</c> never happens, so <c>Top3</c> has no
    /// Calor0410 — before W5 that program compiled clean, laundering <c>cw</c>.
    /// Calor0406 is the only thing standing between that and "Compilation
    /// successful".
    /// </summary>
    [Fact]
    public void PropagateInstantiatedCharges_CapHit_TheUnchargedCallerIsSilent_SoCalor0406_IsTheOnlyError()
    {
        var diagnostics = Enforce(FourHopRank1Chain, chargeCap: 2);

        Assert.DoesNotContain(diagnostics,
            d => d.Code == DiagnosticCode.ForbiddenEffect && d.Message.Contains("'Top3'"));
        var onlyError = Assert.Single(diagnostics.Errors);
        Assert.Equal(DiagnosticCode.EffectInferenceDidNotConverge, onlyError.Code);
        Assert.Contains("'Top2'", onlyError.Message);
    }

    [Fact]
    public void PropagateInstantiatedCharges_CapHit_IsReportedAtTheFirstUnprocessedFunction()
    {
        var (module, diagnostics) = Parse(ThreeHopRank1Chain);
        new EffectEnforcementPass(diagnostics) { InstantiatedChargeIterationCap = 2 }.Enforce(module);

        var reported = Assert.Single(NonConvergence(diagnostics));
        var top2 = Assert.Single(module.Functions, f => f.Name == "Top2");
        Assert.Equal(top2.Span, reported.Span);
    }

    /// <summary>
    /// (c) negative pin: at the default cap the chain converges — no Calor0406 —
    /// and the propagation reached <c>Top2</c>, whose Calor0410 is the proof.
    /// </summary>
    [Fact]
    public void PropagateInstantiatedCharges_DefaultCap_Converges_NoCalor0406()
    {
        var diagnostics = Enforce(ThreeHopRank1Chain);

        Assert.Empty(NonConvergence(diagnostics));
        Assert.Contains(diagnostics,
            d => d.Code == DiagnosticCode.ForbiddenEffect
              && d.Message.Contains("Function 'Top2' uses effect 'cw' but does not declare it"));
    }

    /// <summary>
    /// The four-hop chain needs exactly four dequeues (<c>Outer</c>, <c>Top</c>,
    /// <c>Top2</c>, <c>Top3</c>), so 4 is the smallest cap that converges and the
    /// boundary is pinned on both sides. <c>Top3</c> is charged by the third
    /// dequeue, so caps 1–2 leave it silent, cap 3 charges it but still reports
    /// (Top3 is left queued — the pass does not know it has no callers without
    /// doing the work, and says so), and 4+ is clean. The cap is shown to be the
    /// injected value rather than a constant.
    /// </summary>
    [Theory]
    [InlineData(1, true, false)]
    [InlineData(2, true, false)]
    [InlineData(3, true, true)]
    [InlineData(4, false, true)]
    [InlineData(5, false, true)]
    public void PropagateInstantiatedCharges_CapBoundary(int cap, bool expectReport, bool expectTop3Charged)
    {
        var diagnostics = Enforce(FourHopRank1Chain, chargeCap: cap);

        var top3Charged = diagnostics.Any(
            d => d.Code == DiagnosticCode.ForbiddenEffect && d.Message.Contains("'Top3'"));
        Assert.Equal(expectTop3Charged, top3Charged);

        if (expectReport)
        {
            var reported = Assert.Single(NonConvergence(diagnostics));
            Assert.Contains($"cap of {cap} steps", reported.Message);
        }
        else
        {
            Assert.Empty(NonConvergence(diagnostics));
        }
    }

    /// <summary>
    /// The two caps are independent: injecting one leaves the other loop at its
    /// default, so a fixture that stresses only the SCC loop reports only the SCC
    /// site and vice versa.
    /// </summary>
    [Fact]
    public void Caps_AreIndependent_EachSiteReportsOnlyItself()
    {
        var sccOnly = Enforce(ThreeHopMutualRecursion, sccCap: 2, chargeCap: 1);
        var reportedScc = Assert.Single(NonConvergence(sccOnly));
        Assert.Contains("SCC fixpoint", reportedScc.Message);

        var chargeOnly = Enforce(ThreeHopRank1Chain, sccCap: 1, chargeCap: 2);
        var reportedCharge = Assert.Single(NonConvergence(chargeOnly));
        Assert.Contains("instantiated-charge propagation", reportedCharge.Message);
    }

    // ------------------------------------------------------------------
    // Severity under every policy (review #1 MAJOR 1)
    // ------------------------------------------------------------------

    /// <summary>
    /// "Always an error, and no flag waives it" (effects.md, Diagnostic.cs):
    /// <c>--permissive-effects</c> demotes Calor0410 and silences Calor0425, and
    /// must not touch Calor0406 at either site. <c>HasErrors</c> is what the
    /// driver reads to fail the build.
    /// </summary>
    [Theory]
    [InlineData("scc", UnknownCallPolicy.Permissive)]
    [InlineData("scc", UnknownCallPolicy.Strict)]
    [InlineData("worklist", UnknownCallPolicy.Permissive)]
    [InlineData("worklist", UnknownCallPolicy.Strict)]
    public void Calor0406_IsAnError_UnderEveryPolicy(string site, UnknownCallPolicy policy)
    {
        var diagnostics = site == "scc"
            ? Enforce(ThreeHopMutualRecursion, sccCap: 2, policy: policy)
            : Enforce(ThreeHopRank1Chain, chargeCap: 2, policy: policy);

        var reported = Assert.Single(NonConvergence(diagnostics));
        Assert.Equal(DiagnosticSeverity.Error, reported.Severity);
        Assert.True(diagnostics.HasErrors);
        Assert.Contains(reported, diagnostics.Errors);
    }

    /// <summary>
    /// The control for the theory above: under Permissive the same SCC fixture
    /// with an under-declaring member gets its Calor0410 as a WARNING, so the
    /// flag is doing its job on 0410 while leaving 0406 alone.
    /// </summary>
    [Fact]
    public void Permissive_DemotesCalor0410_ButNotCalor0406()
    {
        var underDeclared = ThreeHopMutualRecursion.Replace(
            "§F{f003:C:pub} (i32:n) -> i32\n    §E{cw,cr,fs:r}",
            "§F{f003:C:pub} (i32:n) -> i32\n    §E{cw,cr}",
            StringComparison.Ordinal);
        Assert.NotEqual(ThreeHopMutualRecursion, underDeclared);

        var diagnostics = Enforce(underDeclared, sccCap: 100, policy: UnknownCallPolicy.Permissive);
        var forbidden = diagnostics.Where(d => d.Code == DiagnosticCode.ForbiddenEffect).ToList();
        Assert.NotEmpty(forbidden);
        Assert.All(forbidden, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));

        var capped = Enforce(underDeclared, sccCap: 2, policy: UnknownCallPolicy.Permissive);
        Assert.Equal(DiagnosticSeverity.Error, Assert.Single(NonConvergence(capped)).Severity);
    }

    // ------------------------------------------------------------------
    // Defaults and the driver path
    // ------------------------------------------------------------------

    /// <summary>
    /// Gate 11's denominator: the two caps at the values the roadmap cites
    /// (<c>:455</c> cap 100, <c>:1129</c> cap 10 000). A pass constructed with
    /// nothing injected — the <c>Program.cs</c> / SDK / MCP / index path — carries
    /// them.
    /// </summary>
    [Fact]
    public void DefaultCaps_AreTheRoadmapValues_AndApplyWhenNothingIsInjected()
    {
        Assert.Equal(100, EffectEnforcementPass.DefaultSccFixpointIterationCap);
        Assert.Equal(10_000, EffectEnforcementPass.DefaultInstantiatedChargeIterationCap);

        var pass = new EffectEnforcementPass(new DiagnosticBag());
        Assert.Equal(EffectEnforcementPass.DefaultSccFixpointIterationCap, pass.SccFixpointIterationCap);
        Assert.Equal(EffectEnforcementPass.DefaultInstantiatedChargeIterationCap, pass.InstantiatedChargeIterationCap);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Caps_RejectValuesBelowOne(int cap)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new EffectEnforcementPass(new DiagnosticBag()) { SccFixpointIterationCap = cap });
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new EffectEnforcementPass(new DiagnosticBag()) { InstantiatedChargeIterationCap = cap });
    }

    /// <summary>
    /// Through the full compiler driver (the path <c>calor build</c> takes), both
    /// fixtures converge at the default caps: no Calor0406 and no Calor0600.
    /// </summary>
    [Theory]
    [InlineData(ThreeHopMutualRecursion)]
    [InlineData(ThreeHopRank1Chain)]
    [InlineData(FourHopRank1Chain)]
    [InlineData(SelfRecursive)]
    public void Driver_DefaultCaps_NoNonConvergenceCodes(string source)
    {
        var result = TestHarness.Compile(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.EffectInferenceDidNotConverge);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.BreakingChangeWithoutMarker);
    }

    // ------------------------------------------------------------------
    // The code itself
    // ------------------------------------------------------------------

    [Fact]
    public void Calor0406_IsReservedInTheEffectsBand()
    {
        Assert.Equal("Calor0406", DiagnosticCode.EffectInferenceDidNotConverge);
        var number = int.Parse(DiagnosticCode.EffectInferenceDidNotConverge["Calor".Length..]);
        Assert.InRange(number, 400, 499);

        // And it is NOT the API-strictness code the SCC site used to borrow.
        Assert.Equal("Calor0600", DiagnosticCode.BreakingChangeWithoutMarker);
        Assert.NotEqual(DiagnosticCode.BreakingChangeWithoutMarker, DiagnosticCode.EffectInferenceDidNotConverge);
    }

    /// <summary>
    /// (d) Calor0600 is retired from the effects pass: neither the quoted string
    /// literal nor the constant that carries it occurs anywhere under
    /// <c>src/Calor.Compiler/Effects/</c>. The constant's NAME is read off
    /// <see cref="DiagnosticCode"/> by reflection (the field whose value is
    /// "Calor0600"), so renaming it cannot silently blind the pin. A source-level
    /// pin, so it catches a reintroduction at any site, not only the two this
    /// item touched; a comment recording the history is fine, an emission is not.
    /// </summary>
    [Fact]
    public void Calor0600_IsNoLongerEmittedAnywhereInTheEffectsPass()
    {
        var effectsDir = Path.Combine(RepoRoot(), "src", "Calor.Compiler", "Effects");
        Assert.True(Directory.Exists(effectsDir), $"Effects directory not found at {effectsDir}");

        var constant = Assert.Single(
            typeof(DiagnosticCode)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string) && (string?)f.GetRawConstantValue() == "Calor0600"));
        var pattern = new Regex($@"""Calor0600""|\b{Regex.Escape(constant.Name)}\b");

        var offenders = Directory
            .EnumerateFiles(effectsDir, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => (File: Path.GetRelativePath(effectsDir, file), Line: index + 1, Text: line))
                .Where(entry => pattern.IsMatch(entry.Text)))
            .Select(entry => $"{entry.File}:{entry.Line}: {entry.Text.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "Calor0600 belongs to the API-strictness band and must not be emitted by the effects pass:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// The same fixture that used to produce the Calor0600 warning at the SCC cap
    /// now produces Calor0406 and nothing under the old code.
    /// </summary>
    [Fact]
    public void SccCapHit_ProducesCalor0406_NotCalor0600()
    {
        var diagnostics = Enforce(ThreeHopMutualRecursion, sccCap: 2);

        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.BreakingChangeWithoutMarker);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.EffectInferenceDidNotConverge);
    }

    /// <summary>
    /// The test assembly runs from <c>tests/Calor.Enforcement.Tests/bin/{cfg}/{tfm}/</c>,
    /// five levels below the repository root — the same walk
    /// <c>EffectsSuggestTests.RepoRoot</c> uses.
    /// </summary>
    private static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
