using System.Text.Json;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Parsing;
using Xunit;

// Flat test namespace: see BinderDispatchCompletenessTests.
namespace Calor.Compiler.Tests;

/// <summary>
/// v0.15 E3 slice a — pin <b>P32</b>, <c>Calor0425CorpusLedgerMatchesRecomputation</c>
/// (design-doc §13.2), and the instrument §13.4 registers as a DECISION rather
/// than an open question.
///
/// <para><b>Why this number matters.</b> 431 of 1248 BCL call sites do not
/// resolve (§2.2). If the Calor0425 count per converted subject runs into the
/// hundreds, effect rows are ergonomically <i>worse</i> than Calor0418 for
/// converted code and <c>--permissive-effects</c> becomes mandatory rather than
/// exceptional — which would make §4.5's "strictly less powerful waiver"
/// decision land badly. §13.4's whole point is that this is measured before it
/// is argued about.</para>
///
/// <para><b>What slice a can and cannot measure, stated so the ledger is not
/// overread.</b> §13.4 names three causes — unresolved receiver, row-less
/// function-typed declaration, BCL-returned delegate. Two of the three are
/// <b>E4's</b>: they become Calor0425 only when INVOKING a row-less value stops
/// being Calor0418. What slice a emits is the five binding sites of §6.2, so the
/// causes this ledger splits by are the causes those sites can distinguish:
/// <list type="bullet">
/// <item><c>RowlessDestination</c> — the destination position carries no row at
/// all (§6.4's second message sample). This is §13.4's "row-less function-typed
/// declaration", seen from the binding side.</item>
/// <item><c>UnknownSource</c> — the value flowing in has an Unknown row against
/// a destination that declares one. This is §13.4's "unresolved receiver" and
/// "BCL-returned delegate" as they reach a SITE; the two are indistinguishable
/// from here, because both arrive as the same Unknown.</item>
/// <item><c>Assumed</c> — the row FITS, but only under an assumption whose
/// reasons the hop carries (§4.3).</item>
/// </list>
/// <b>v0.15 E4 widened the split</b>, as §13.4 and the paragraph above said it
/// must, to the causes the pass can now distinguish (schema 2):
/// <list type="bullet">
/// <item><c>ExternalBase</c> — §6.4's third sample: an override or interface
/// implementation reaching an external base (E3b's 0419 → 0425 retirement).
/// These were the four <c>UnknownSource</c> entries of schema 1; they are not
/// a SOURCE row at a binding site at all, and the ELSE arm mislabelled them.</item>
/// <item><c>InvocationRowless</c> — E4's invocation of a function-typed value
/// declared with no row (§13.4's "row-less function-typed declaration", seen
/// from the INVOKING side, where it costs something).</item>
/// <item><c>InvocationUndetermined</c> — E4's invocation of a value whose row
/// cannot be determined from its initializer or its producing call (§13.4's
/// "BCL-returned delegate": the value came back from a callee this pass cannot
/// see, or from a return that carries no row).</item>
/// <item><c>InvocationAssumed</c> — E4's invocation of an <c>Assumed</c> row,
/// charged and reported once.</item>
/// </list>
/// §13.4's "unresolved receiver" is NOT a bucket, and honestly so: an invoked
/// value whose type came from an unresolved receiver never reaches Calor0425 —
/// the bare-target guard sends it through the unknown-call chain as Calor0411
/// (E1 slice 2c), which is the older fail-closed path and is not this ledger's.</para>
///
/// <para><b>The fourth split ships too</b> (§13.4's closing paragraph): for each
/// row-less destination, whether the position is ever INVOKED in its module or
/// merely declared. That is exactly the number §14 Q4 needs, and it is what
/// tells a reader whether the 0425s are load-bearing or ceremonial.</para>
///
/// <para><b>Exact equality, both directions.</b> A rise means the row-less
/// surface grew or the resolution ceiling fell; a fall means a site went silent.
/// Both are decisions and both are made in a PR that regenerates this file and
/// says what moved. Regenerate with
/// <c>CALOR_REGENERATE_CALOR0425_LEDGER=1 dotnet test --filter Calor0425CorpusLedger</c>.
/// A MISSING ledger is a failure, never a silent regeneration.</para>
///
/// <para>Skipped when the corpus submodules are not initialized; the
/// <c>compiler</c> shard checks them out, and the skip is registered in
/// <c>eng/test-manifest.json</c>'s <c>expectedSkipped</c> so a silent skip trips
/// the count.</para>
///
/// <para><b>v0.16 K1 — schema 3: the ledger now mirrors the SHIPPING rule.</b>
/// Schema 2 gated the effect pass on the <i>raw</i> binder bag
/// (<c>new Binder(bag).Bind(module); if (bag.HasErrors) skip</c>). The shipping
/// compiler does not: it filters the binder's bag through
/// <c>BindingDiagnosticPolicy.PropagateCompilationErrors</c>
/// (<c>Program.cs:820</c>; allowlist <c>Binding/Scope.cs:53-78</c>) into the
/// compilation's own bag and only then stops (<c>Program.cs:829-833</c>).
/// Calor0200 / Calor0272 / Calor0273 / the #1097 ICE are never propagated, so a
/// module carrying only those compiles for a real user and its Calor0425s are
/// diagnostics a real user sees. Schema 2's "8 sites over 99 enforced modules"
/// was therefore an artifact of the measurement's own guard, not a fact about
/// the compiler; schema 3 measures over the modules the compiler actually
/// reaches. Both denominators are kept in the ledger
/// (<c>ModulesEnforced</c> under the production rule,
/// <c>ModulesEnforcedRawBagRule</c> under schema 2's), which is §3.1 K1's
/// discriminating pin made permanent: restoring the raw-bag guard drives
/// <c>ModulesEnforced</c> back to the raw-bag number and the equality below goes
/// red. See <c>docs/plans/roadmap-v0.16.md</c> §0.1, §0.4, §3.1 K1, §5 gate 9
/// and <c>docs/plans/2026-08-27-v0.16-s1-s2-measurement-notes.md</c> §S2.</para>
///
/// <para><b>Manifest rule (K1's scratch-cwd pin).</b> N:S2.2's CLI pass ran in a
/// scratch cwd so that no <c>.calor-effects.json</c> sat beside the input. The
/// in-process pass matches it: it is constructed with no project directory (so
/// no project-local manifest is ever loaded, whatever the test host's cwd is)
/// and with a hermetic <see cref="Calor.Compiler.Effects.Manifests.ManifestLoader"/>
/// (so <c>~/.calor/manifests/</c> cannot make the number depend on the machine).
/// <c>K1_ScratchCwdRule_ProjectLocalManifestBesideTheInputIsNotConsulted</c>
/// observes both halves.</para>
/// </summary>
public class Calor0425CorpusLedgerTests
{
    // v0.16 K1: schema 3 = schema 2 + BindRule + FloorRule + the raw-bag
    // denominator kept beside the production one + the exclusion split that the
    // widened denominator made worth separating (effect-pass faults).
    private const int SchemaVersion = 3;

    /// <summary>
    /// v0.16 K1 — the rule the denominator is measured under, written into the
    /// ledger so it can never again be inferred from the test body.
    /// <c>"propagated"</c> is the shipping compiler's
    /// (<c>Program.cs:820</c> → <c>:829-833</c>). The Calor0270 ledger records
    /// <c>"parsed"</c> for its own, weaker rule, so the two are named side by
    /// side and neither can be read as the other.
    /// </summary>
    internal const string BindRuleText = "propagated";

    private const string ScopeText =
        "Calor0425 (EffectRowUnknown) emitted by EffectEnforcementPass over the three A-1.5.3 "
        + "conversion subjects at their pinned submodule commits, converted in-process with "
        + "Lossy/SelectActiveBranchLossy and genuinely empty default preprocessor symbols, then "
        + "enforced with UnknownCallPolicy.Strict and no --permissive-effects; the pass runs over "
        + "every module the SHIPPING COMPILER reaches — binder diagnostics are filtered through "
        + "BindingDiagnosticPolicy.PropagateCompilationErrors into a fresh bag before the stop, "
        + "exactly as Program.cs:820/829-833 does (v0.16 K1, schema 3), NOT gated on the raw "
        + "binder bag as schema 2 was; modules that fail conversion, whose converted output fails "
        + "to parse, that stop at a propagated binding error, or whose effect pass throws are "
        + "excluded from the denominator and counted separately. Manifests are built-in only: the "
        + "pass is constructed with no project directory, so no .calor-effects.json beside the "
        + "input is consulted whatever the cwd is, and the loader is hermetic, so "
        + "~/.calor/manifests cannot move the number — the same manifest state as the pinned CLI "
        + "pass's scratch cwd (N:S2.2). Causes are the ones the FIVE MONOMORPHIC SITES of "
        + "design-doc §6.2 can distinguish, plus (schema 2, v0.15 E4) the external-base arm of "
        + "sites 4/5 and the three verdicts an INVOCATION of a function-typed value can draw — "
        + "row-less declaration, undetermined source (a BCL-returned or row-less-returned value), "
        + "and an Assumed row; §13.4's unresolved-receiver cause is Calor0411's, not this ledger's";

    private static readonly string[] Subjects = ["MediatR", "serilog", "FluentValidation"];

    /// <summary>
    /// v0.16 §5 gate 9's floor, registered here (and written into the ledger) by
    /// K1's PR "before any W3 fix merges", as the gate requires.
    ///
    /// <para><b>Two legs, and they are honest about being at different stages.</b>
    /// The <c>ModulesEnforced ≥ 250</c> leg is a live regression floor: it holds
    /// today (the aggregate is 256) and <c>Gate9_ModulesEnforcedFloor_Holds</c>
    /// enforces it now. The <c>ExcludedParseFailed ≤ 2</c> leg is the
    /// USER-VISIBLE bar and it does NOT hold today — the observed value is 59,
    /// and #903 clusters 1–2 (W3(a), PR #1125, which merges AFTER K1) are what
    /// recover 57 of them. Rather than weaken the gate to something K1 can pass,
    /// the registered floor is written down as-is together with the value
    /// observed at registration and the item it waits on
    /// (<c>ExcludedParseFailedPendingUntil</c>), and
    /// <c>Gate9_ExcludedParseFailedLeg_PinsTheRegistrationValueUntilW3a</c> pins
    /// the CURRENT value EXACTLY — so W3(a)'s merge is what turns the pin red and
    /// forces the flip, and no one can quietly regress the parse-failure count in
    /// the meantime either.</para>
    /// </summary>
    private static Gate9FloorRule RegisteredFloorRule() => new(
        "roadmap-v0.16.md §5 gate 9 — conversion denominator, re-set on the production rule",
        BindRuleText,
        250,
        [
            new SubjectFloor("MediatR", 29),
            new SubjectFloor("serilog", 84),
            new SubjectFloor("FluentValidation", 137),
        ],
        2,
        59,
        "W3(a)",
        "ModulesEnforced >= 250 is a live regression floor (six below the 256 observed at "
        + "registration; the per-subject MediatR/serilog floors are EXACT and the slack sits in "
        + "FluentValidation). ExcludedParseFailed <= 2 is the registered user-visible bar and is "
        + "NOT met at registration: 59 modules fail to parse, and W3(a) (#903 clusters 1-2, "
        + "PR #1125) recovers 57 of them. Until that merges the aggregate is pinned EXACTLY at "
        + "ExcludedParseFailedRegisteredAt, which is what makes W3(a)'s merge flip this rule "
        + "rather than silently satisfying a weakened one. When the observed value drops to "
        + "<= ExcludedParseFailedMax, ExcludedParseFailedPendingUntil must be cleared to the "
        + "empty string in the same PR — the test asserts that consistency.");

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Directory.Build.props")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }

    private static string LedgerPath() => Path.Combine(RepoRoot(),
        "bench", "phase0-agent-native", "calor0425-corpus-ledger.json");

    [SkippableFact]
    public void Calor0425CorpusLedgerMatchesRecomputation()
    {
        var root = RepoRoot();
        var subjectDirs = Subjects
            .Select(subject => (Name: subject, Path: Path.Combine(root, "bench", "corpus", subject, "src")))
            .ToList();
        Skip.IfNot(subjectDirs.All(s => Directory.Exists(s.Path)),
            "corpus submodules not initialized");

        var perSubject = subjectDirs.Select(s => MeasureSubject(s.Name, s.Path)).ToList();
        foreach (var subject in perSubject)
        {
            Console.WriteLine(
                $"Calor0425-corpus {subject.Subject}: {subject.Diagnostics} across "
                + $"{subject.ModulesWithDiagnostics} of {subject.ModulesEnforced} enforced modules "
                + $"(rowless {subject.RowlessDestination}, unknown-source {subject.UnknownSource}, "
                + $"assumed {subject.Assumed}, external-base {subject.ExternalBase}, "
                + $"invocation rowless {subject.InvocationRowless} / undetermined "
                + $"{subject.InvocationUndetermined} / assumed {subject.InvocationAssumed}; "
                + $"of the rowless, invoked {subject.RowlessInvoked} / "
                + $"never invoked {subject.RowlessNeverInvoked}; invocation witness "
                + $"{subject.InvocationWitness}; excluded {subject.ModulesNotMeasured} "
                + $"= convert {subject.ExcludedConversionFailed} / parse {subject.ExcludedParseFailed} "
                + $"/ propagated-bind {subject.ExcludedBindFailed} / faulted "
                + $"{subject.ExcludedEffectPassFaulted}; raw-bag rule would have enforced "
                + $"{subject.ModulesEnforcedRawBagRule} and excluded {subject.RawBagBindFailed} "
                + "at bind)");
        }

        var measured = new Ledger(
            SchemaVersion,
            ScopeText,
            BindRuleText,
            RegisteredFloorRule(),
            MeasuredCommit(root),
            perSubject.Sum(s => s.Diagnostics),
            perSubject.Sum(s => s.ModulesWithDiagnostics),
            perSubject.Sum(s => s.ModulesEnforced),
            perSubject.Sum(s => s.ModulesNotMeasured),
            perSubject);

        Console.WriteLine(
            $"Calor0425-corpus aggregate: {measured.AggregateDiagnostics} across "
            + $"{measured.AggregateModulesWithDiagnostics} of {measured.AggregateModulesEnforced} "
            + $"(raw-bag rule: {perSubject.Sum(s => s.ModulesEnforcedRawBagRule)} enforced; "
            + $"parse-failed {perSubject.Sum(s => s.ExcludedParseFailed)}; propagated bind-failed "
            + $"{perSubject.Sum(s => s.ExcludedBindFailed)}; faulted "
            + $"{perSubject.Sum(s => s.ExcludedEffectPassFaulted)}; UnknownSource+"
            + "InvocationUndetermined "
            + $"{perSubject.Sum(s => s.UnknownSource + s.InvocationUndetermined)})");

        var ledgerPath = LedgerPath();
        if (string.Equals(
                Environment.GetEnvironmentVariable("CALOR_REGENERATE_CALOR0425_LEDGER"),
                "1", StringComparison.Ordinal))
        {
            File.WriteAllText(ledgerPath, JsonSerializer.Serialize(
                measured, new JsonSerializerOptions { WriteIndented = true }) + "\n");
            Console.WriteLine($"Calor0425 corpus ledger regenerated: {ledgerPath}");
            return;
        }

        Assert.True(File.Exists(ledgerPath),
            $"Calor0425 corpus ledger missing at {ledgerPath} — run once with "
            + "CALOR_REGENERATE_CALOR0425_LEDGER=1. A missing ledger is a FAILURE, not a cue to "
            + "write one silently: the number is the instrument §13.4 registers.");
        var committed = JsonSerializer.Deserialize<Ledger>(File.ReadAllText(ledgerPath))!;

        // Anti-vacuity, now tied to gate 9's own floor rather than a hand-picked
        // number: a ledger that enforced nothing would satisfy every equality
        // below, including a per-subject zero. v0.16 K1 raised this from 90
        // (schema 2's raw-bag denominator was 99) to the registered floor,
        // because under the production rule the denominator is 256.
        var floor = RegisteredFloorRule();
        Assert.True(measured.AggregateModulesEnforced >= floor.ModulesEnforcedMin,
            $"Only {measured.AggregateModulesEnforced} modules were enforced under the "
            + $"production bind rule — gate 9's floor is {floor.ModulesEnforcedMin}. Either the "
            + "corpus denominator collapsed (making the equalities below vacuous) or the "
            + "measurement stopped mirroring the shipping compiler — the raw-bag guard schema 2 "
            + $"used would report {measured.PerSubject.Sum(s => s.ModulesEnforcedRawBagRule)} "
            + "here (roadmap-v0.16.md §3.1 K1's discriminating pin).");

        // THE EXCLUSION RATE IS PART OF THE MEASUREMENT, not a footnote to it.
        // 265 of 364 modules (73%) never reach the effect pass, almost all of
        // them because the Lossy conversion does not BIND — FluentValidation
        // alone contributes 190 of them, leaving 26 enforced. A zero measured
        // over the 27% that binds is a much weaker statement than a zero over
        // the corpus, and pinning the rate is what stops the next reader (or the
        // next regeneration) from forgetting that.
        Assert.Equal(committed.AggregateModulesExcluded, measured.AggregateModulesExcluded);
        Assert.True(measured.AggregateModulesExcluded > 0,
            "Zero exclusions would mean the conversion+bind gate stopped filtering, which "
            + "changes what the headline zero is a zero OVER.");

        // The number this ledger records is only worth recording if the pass ran
        // and SAW higher-order code. Pre-E4 the witness was FOUR Calor0418 across
        // all three subjects (2/1/1), not "hundreds"; E4 retired that code for
        // function-typed values, and the witness is now the number of INVOCATIONS
        // of function-typed values the pass adjudicated — which in converted code,
        // where no row is ever written, is exactly the invocation-bucket
        // Calor0425s. Still a weak witness and still written down as one: it
        // establishes that the pass reached higher-order code at all, and it does
        // NOT establish that the measured subset is representative of the corpus.
        // Read the exclusion rate below before drawing any conclusion.
        Assert.True(measured.PerSubject.Sum(s => s.InvocationWitness) > 0,
            "No invocation of a function-typed value anywhere in the measured corpus — the "
            + "effect pass did not reach the higher-order code it is supposed to be measuring, "
            + "so the Calor0425 counts would mean nothing.");

        Assert.Equal(SchemaVersion, committed.SchemaVersion);
        Assert.Equal(ScopeText, committed.Scope);

        // v0.16 K1 — the bind rule and gate 9's floor rule travel WITH the
        // numbers, so a reader of the JSON alone knows what denominator they are
        // looking at. Regenerating under a different rule without saying so is
        // exactly the failure K1 exists to correct.
        Assert.Equal(BindRuleText, committed.BindRule);
        AssertFloorRuleEqual(RegisteredFloorRule(), committed.FloorRule);

        // measuredCommit is SHAPE-checked, never compared to HEAD — the
        // convention the two existing ledgers use
        // (HigherOrderDemandLedgerTests.cs:480-498), because a ledger regenerated
        // in a PR is stamped with a commit that does not exist until it merges.
        Assert.Matches("^[0-9a-f]{40}$", committed.MeasuredCommit);

        Assert.Equal(committed.PerSubject.Count, measured.PerSubject.Count);
        foreach (var (expected, actual) in committed.PerSubject.Zip(measured.PerSubject))
        {
            Assert.Equal(expected.Subject, actual.Subject);
            Assert.True(expected == actual,
                $"Calor0425 volume moved for {actual.Subject}.\n"
                + $"  committed: {expected}\n"
                + $"  measured : {actual}\n"
                + "A RISE means the row-less surface grew or the resolution ceiling fell; a FALL "
                + "means a site went silent. Both are decisions. Regenerate the ledger IN THIS PR "
                + "with CALOR_REGENERATE_CALOR0425_LEDGER=1 and name the cause — never absorb it.");
        }

        Assert.Equal(committed.AggregateDiagnostics, measured.AggregateDiagnostics);
        Assert.Equal(committed.AggregateModulesWithDiagnostics, measured.AggregateModulesWithDiagnostics);
        Assert.Equal(committed.AggregateModulesEnforced, measured.AggregateModulesEnforced);
    }

    // ---------------------------------------------------------------------
    // v0.16 K1 — the gate-9 pins. These read the COMMITTED ledger only, so they
    // run on a bare clone: the ledger's numbers are tied to the corpus by
    // Calor0425CorpusLedgerMatchesRecomputation above, and what these assert is
    // that the floor gate 9 registers is written down, is consistent, and holds.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Gate 9's floor rule exists in the ledger, names the production bind rule,
    /// and matches the registration in <see cref="RegisteredFloorRule"/>. §5
    /// gate 9 requires K1's PR to write <c>floorRule</c> "before any W3 fix
    /// merges" — this is the assertion that it did.
    /// </summary>
    [Fact]
    public void Gate9_FloorRule_IsRegisteredInTheCommittedLedger()
    {
        var committed = CommittedLedger();

        Assert.Equal(SchemaVersion, committed.SchemaVersion);
        Assert.Equal(BindRuleText, committed.BindRule);
        AssertFloorRuleEqual(RegisteredFloorRule(), committed.FloorRule);
        Assert.Equal(BindRuleText, committed.FloorRule.BindRule);
    }

    /// <summary>
    /// Gate 9's LIVE leg: <c>ModulesEnforced ≥ 250</c> in aggregate and
    /// ≥ the per-subject floor (MediatR 29 and serilog 84 are EXACT today;
    /// FluentValidation 137 sits six below its 143). A regression floor, and it
    /// is enforced now — nothing about it waits on W3(a).
    /// </summary>
    [Fact]
    public void Gate9_ModulesEnforcedFloor_Holds()
    {
        var committed = CommittedLedger();
        var floor = committed.FloorRule;

        Assert.True(committed.AggregateModulesEnforced >= floor.ModulesEnforcedMin,
            $"Gate 9: {committed.AggregateModulesEnforced} modules enforced under the "
            + $"'{committed.BindRule}' bind rule, floor {floor.ModulesEnforcedMin}. A fall is "
            + "either a real conversion/binding regression or a measurement that stopped "
            + "mirroring the shipping compiler; roadmap §5 gate 9 names the NOT-ADJUDICATED "
            + "route for the second case (a documented CLI-only pass), and it requires the "
            + "floor to be re-registered from the new number WITH the artifact — never absorbed.");

        foreach (var subjectFloor in floor.PerSubjectModulesEnforcedMin)
        {
            var subject = committed.PerSubject.Single(s => s.Subject == subjectFloor.Subject);
            Assert.True(subject.ModulesEnforced >= subjectFloor.ModulesEnforcedMin,
                $"Gate 9, per subject: {subject.Subject} enforced {subject.ModulesEnforced}, "
                + $"floor {subjectFloor.ModulesEnforcedMin}.");
        }

        Assert.Equal(
            committed.AggregateModulesEnforced,
            committed.PerSubject.Sum(s => s.ModulesEnforced));
    }

    /// <summary>
    /// Gate 9's PENDING leg, in the only honest form available before W3(a)
    /// merges. The registered bar is <c>ExcludedParseFailed ≤ 2</c>; the value
    /// today is 59, and #903 clusters 1–2 (W3(a), PR #1125 — which merges AFTER
    /// K1) recover 57 of them. So the bar is recorded as registered, and the
    /// CURRENT value is pinned EXACTLY: W3(a)'s merge is what turns this red and
    /// forces the flip, and a regression in the meantime turns it red too. When
    /// the observed value finally meets the bar, <c>PendingUntil</c> must be
    /// cleared in the same PR — the last assertion is that consistency, so the
    /// pending state cannot outlive the thing it waits on.
    /// </summary>
    [Fact]
    public void Gate9_ExcludedParseFailedLeg_PinsTheRegistrationValueUntilW3a()
    {
        var committed = CommittedLedger();
        var floor = committed.FloorRule;
        var observed = committed.PerSubject.Sum(s => s.ExcludedParseFailed);

        Assert.Equal(floor.ExcludedParseFailedRegisteredAt, observed);

        if (observed <= floor.ExcludedParseFailedMax)
        {
            Assert.True(floor.ExcludedParseFailedPendingUntil.Length == 0,
                $"ExcludedParseFailed is {observed}, at or under the registered bar of "
                + $"{floor.ExcludedParseFailedMax}, but the ledger still says it is pending on "
                + $"'{floor.ExcludedParseFailedPendingUntil}'. Clear "
                + "ExcludedParseFailedPendingUntil in the PR that met the bar.");
        }
        else
        {
            Assert.Equal("W3(a)", floor.ExcludedParseFailedPendingUntil);
            Assert.True(observed > floor.ExcludedParseFailedMax,
                "unreachable — kept so the branch reads as the pending state it is");
        }
    }

    /// <summary>
    /// §3.1 K1's <b>discriminating pin</b>, made permanent instead of run by
    /// hand: the ledger carries BOTH denominators, and restoring schema 2's
    /// raw-bag guard would make the production one collapse onto the raw-bag one.
    /// If that ever happened, this test and
    /// <see cref="Gate9_ModulesEnforcedFloor_Holds"/> both go red.
    /// </summary>
    [Fact]
    public void Gate9_BothBindRules_AreRecorded_AndTheProductionOneIsWider()
    {
        var committed = CommittedLedger();
        var production = committed.AggregateModulesEnforced;
        var rawBag = committed.PerSubject.Sum(s => s.ModulesEnforcedRawBagRule);

        Assert.True(rawBag > 0, "the raw-bag denominator is not recorded");
        Assert.True(production > rawBag,
            $"The production rule enforces {production} modules and the raw-bag rule "
            + $"{rawBag}. The production rule can only be WIDER — every module the raw bag "
            + "accepts, the propagated filter accepts too. Equality means the raw-bag guard is "
            + "back (roadmap §3.1 K1's discriminating pin: ModulesEnforced 256 → 99 → red).");

        // The four exclusion reasons account for every excluded module, and the
        // raw-bag rule's own exclusion count is recorded beside them.
        foreach (var subject in committed.PerSubject)
        {
            Assert.Equal(
                subject.ModulesNotMeasured,
                subject.ExcludedConversionFailed + subject.ExcludedParseFailed
                    + subject.ExcludedBindFailed + subject.ExcludedEffectPassFaulted);
            Assert.True(subject.RawBagBindFailed >= subject.ExcludedBindFailed,
                $"{subject.Subject}: the raw bag rejected {subject.RawBagBindFailed} modules and "
                + $"the propagated rule {subject.ExcludedBindFailed}; the propagated set is a "
                + "SUBSET of the raw one by construction (the filter only removes diagnostics).");
        }
    }

    /// <summary>
    /// K1 and the Calor0270 ledger name their bind rules side by side, which is
    /// the whole point of touching the Calor0270 schema at all (roadmap §3.1 K1,
    /// §6 row 2). The Calor0270 ledger counts Infos from the RAW bag over every
    /// module that PARSES — 305 — and is deliberately not regenerated by K1.
    /// </summary>
    [Fact]
    public void K1_BothLedgers_NameTheirOwnBindRule()
    {
        var calor0425 = CommittedLedger();
        using var calor0270 = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepoRoot(), "bench", "phase0-agent-native", "calor0270-corpus-ledger.json")));

        var otherRule = calor0270.RootElement.GetProperty("BindRule").GetString();

        Assert.Equal("propagated", calor0425.BindRule);
        Assert.Equal("parsed", otherRule);
        Assert.NotEqual(calor0425.BindRule, otherRule);
    }

    /// <summary>
    /// <b>K1's sequencing pin</b> (roadmap §3.1 K1, §6 row "#1104 recursion"):
    /// W3(c) lands before K1. K1 removed the bind-first guard, so the in-process
    /// effect pass now runs over ~157 modules the ledger has never enforced —
    /// including the two Serilog modules that took the whole test host down
    /// (#1104). A StackOverflowException is a .NET fail-fast: no catch block on
    /// any thread observes it, so it would not fail this suite, it would DELETE
    /// it, taking every other pin's verdict with it. The CLI shows zero crashes
    /// over the 256 (N:S2.2) because the two known crashers stop at a propagated
    /// Calor0250 there; the residual risk is in-process only, which is exactly
    /// this file. W3(c)'s depth bound and its crash-repro pin are what make the
    /// measurement above safe to run, so their absence is a hard error here
    /// rather than a comment nobody reads.
    /// </summary>
    [Fact]
    public void K1_IsSequencedAfterW3c_RecursionPinExists()
    {
        var pin = Path.Combine(RepoRoot(),
            "tests", "Calor.Enforcement.Tests", "EffectInferrerRecursionTests.cs");

        Assert.True(File.Exists(pin),
            $"v0.16 W3(c)'s recursion pin is missing at {pin}. K1's widened denominator hands "
            + "the nested EffectInferrer ~157 modules the ledger never enforced; without the "
            + "depth bound that pin guards, one of them can overflow the stack, and a stack "
            + "overflow kills the test host instead of failing a test. Restore W3(c) before "
            + "this ledger runs (roadmap-v0.16.md §3.1 K1, 'Sequencing pin').");
    }

    /// <summary>
    /// <b>K1's scratch-cwd pin.</b> N:S2.2's CLI pass ran one process per module
    /// in a scratch cwd precisely so no <c>.calor-effects.json</c> sat beside the
    /// input; the in-process measurement has to match, or the two halves of the
    /// S2 cross-check are not measuring the same compiler.
    ///
    /// <para>Three assertions, in the order that makes the pin non-vacuous:
    /// (1) the manifest is LOAD-BEARING — hand the pass that same directory as
    /// its project directory and the module's unknown-call diagnostic goes away;
    /// (2) the ledger's own construction, with the process cwd set to the
    /// directory holding that manifest, does NOT pick it up; (3) the hermetic
    /// loader the ledger uses reports no project-local or user-level source at
    /// all. Without (1) the test would pass against a manifest that never did
    /// anything.</para>
    /// </summary>
    [Fact]
    public void K1_ScratchCwdRule_ProjectLocalManifestBesideTheInputIsNotConsulted()
    {
        // A call to a type no built-in manifest covers. Unresolved, the strict
        // policy fails closed with Calor0411; the manifest below resolves it to
        // the effect the caller declares, so a pass that reads the manifest is
        // silent and a pass that does not is not.
        const string source = """
            §M{m001:ScratchCwd}
              §F{f001:Main:pub} () -> void
                §E{cw}
                §C{K1ScratchWidget.Ping} §/C
            """;

        var scratch = Path.Combine(Path.GetTempPath(), "calor-k1-cwd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var originalCwd = Directory.GetCurrentDirectory();
        try
        {
            File.WriteAllText(Path.Combine(scratch, ".calor-effects.json"), """
                {
                  "version": "1.0",
                  "description": "K1 scratch-cwd pin — must never be consulted by the ledger",
                  "mappings": [
                    { "type": "K1ScratchWidget", "methods": { "Ping": ["cw"] } }
                  ]
                }
                """);

            // (1) The manifest is load-bearing: passed as the project directory,
            //     it resolves the call and the unknown-call diagnostic disappears.
            var withManifest = EnforceOnce(source, projectDirectory: scratch, hermetic: false);
            Assert.DoesNotContain(withManifest, d => d.Code == DiagnosticCode.UnknownExternalCall);

            // (2) The ledger's construction ignores it even from the same cwd.
            Directory.SetCurrentDirectory(scratch);
            var asTheLedgerRunsIt = EnforceOnce(source, projectDirectory: null, hermetic: true);
            Assert.Contains(asTheLedgerRunsIt, d => d.Code == DiagnosticCode.UnknownExternalCall);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            try { Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
        }

        // (3) …and the loader behind it carries built-in manifests only.
        var loader = new Compiler.Effects.Manifests.ManifestLoader(loadUserLevelManifests: false);
        loader.LoadAll(projectDirectory: null, solutionDirectory: null);
        Assert.NotEmpty(loader.LoadedManifests);
        Assert.All(loader.LoadedManifests, entry => Assert.Equal(
            Compiler.Effects.Manifests.ManifestPriority.BuiltIn, entry.Source.Priority));
    }

    private static IReadOnlyList<Diagnostic> EnforceOnce(
        string source, string? projectDirectory, bool hermetic)
    {
        var parseDiagnostics = new DiagnosticBag();
        var module = new Parser(
            new Lexer(source, parseDiagnostics).TokenizeAllForParser(),
            parseDiagnostics).Parse();
        Assert.False(parseDiagnostics.HasErrors,
            "the scratch-cwd pin's fixture must parse: "
            + string.Join(", ", parseDiagnostics.Select(d => $"{d.Code} {d.Message}")));

        var effectDiagnostics = new DiagnosticBag();
        new EffectEnforcementPass(
                effectDiagnostics,
                resolver: hermetic ? HermeticResolver() : null,
                projectDirectory: projectDirectory)
            .Enforce(module);
        return effectDiagnostics.ToList();
    }

    private static Ledger CommittedLedger()
    {
        var path = LedgerPath();
        Assert.True(File.Exists(path),
            $"Calor0425 corpus ledger missing at {path} — run once with "
            + "CALOR_REGENERATE_CALOR0425_LEDGER=1.");
        return JsonSerializer.Deserialize<Ledger>(File.ReadAllText(path))!;
    }

    private static void AssertFloorRuleEqual(Gate9FloorRule expected, Gate9FloorRule actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Gate, actual.Gate);
        Assert.Equal(expected.BindRule, actual.BindRule);
        Assert.Equal(expected.ModulesEnforcedMin, actual.ModulesEnforcedMin);
        Assert.Equal(expected.PerSubjectModulesEnforcedMin, actual.PerSubjectModulesEnforcedMin);
        Assert.Equal(expected.ExcludedParseFailedMax, actual.ExcludedParseFailedMax);
        Assert.Equal(
            expected.ExcludedParseFailedRegisteredAt, actual.ExcludedParseFailedRegisteredAt);
        Assert.Equal(
            expected.ExcludedParseFailedPendingUntil, actual.ExcludedParseFailedPendingUntil);
        Assert.Equal(expected.Note, actual.Note);
    }

    private static string MeasuredCommit(string root)
    {
        // A shallow CI checkout still has HEAD; a worktree's .git is a FILE, so
        // read the ref rather than assuming a directory layout. Falls back to
        // forty zeroes, which the shape check accepts and a reader can spot.
        try
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse HEAD",
                WorkingDirectory = root,
                RedirectStandardOutput = true,
            })!;
            var sha = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return sha.Length == 40 ? sha : new string('0', 40);
        }
        catch
        {
            return new string('0', 40);
        }
    }

    private static SubjectVolume MeasureSubject(string name, string srcRoot)
    {
        var files = Directory
            .EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToArray();

        var parseOptions = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
            Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview,
            Microsoft.CodeAnalysis.DocumentationMode.Parse,
            Microsoft.CodeAnalysis.SourceCodeKind.Regular,
            preprocessorSymbols: Array.Empty<string>());

        int diagnostics = 0, modulesWith = 0, enforced = 0, notMeasured = 0;
        int rowless = 0, unknownSource = 0, assumed = 0, rowlessInvoked = 0, rowlessNever = 0;
        int externalBase = 0, invocationRowless = 0, invocationUndetermined = 0, invocationAssumed = 0;
        // F7 — WHY a module was excluded, not just how many were.
        int excludedConversionFailed = 0, excludedParseFailed = 0, excludedBindFailed = 0;
        // v0.16 K1 (schema 3). `excludedEffectPassFaulted` was folded into
        // `excludedBindFailed` under schema 2, which was tolerable when the bind
        // guard kept the pass away from anything hard; under the production rule
        // the pass sees ~157 more modules, so an ordinary exception must be
        // visible as itself. `enforcedRawBagRule` / `rawBagBindFailed` keep
        // schema 2's denominator beside the production one — §3.1 K1's
        // discriminating pin, computed in the same walk rather than by a
        // hand-run mutation.
        int excludedEffectPassFaulted = 0, enforcedRawBagRule = 0, rawBagBindFailed = 0;

        foreach (var file in files)
        {
            Compiler.Migration.ConversionResult conversion;
            try
            {
                conversion = new Compiler.Migration.CSharpToCalorConverter(
                    new Compiler.Migration.ConversionOptions
                    {
                        Fidelity = Compiler.Migration.ConversionFidelity.Lossy,
                        PreprocessorMode = Compiler.Migration.PreprocessorConversionMode
                            .SelectActiveBranchLossy,
                        ParseOptions = parseOptions,
                        DefinedSymbols = Array.Empty<string>(),
                        ModuleName = "Calor0425Leg",
                        GracefulFallback = true,
                        AutoGenerateIds = true
                    }).Convert(File.ReadAllText(file), Path.GetFileName(file));
            }
            catch
            {
                notMeasured++;
                excludedConversionFailed++;
                continue;
            }

            if (string.IsNullOrEmpty(conversion.CalorSource))
            {
                notMeasured++;
                excludedConversionFailed++;
                continue;
            }

            var source = conversion.CalorSource.Replace("\r\n", "\n");
            var parseDiagnostics = new DiagnosticBag();
            var module = new Parser(
                new Lexer(source, parseDiagnostics).TokenizeAllForParser(),
                parseDiagnostics).Parse();
            if (parseDiagnostics.HasErrors)
            {
                notMeasured++;
                excludedParseFailed++;
                continue;
            }

            var effectDiagnostics = new DiagnosticBag();

            // BIND FIRST, and skip a module the SHIPPING COMPILER would stop on.
            // `Program.Compile` binds into a SEPARATE bag, copies across only the
            // diagnostics `BindingDiagnosticPolicy.IsCompilationError` accepts
            // (`Program.cs:820`; allowlist `Binding/Scope.cs:53-78`), and returns
            // only if the compilation bag then has errors (`Program.cs:829-833`).
            // So a module whose binder bag holds nothing but Calor0200 /
            // Calor0272 / Calor0273 / the #1097 ICE DOES reach the effect pass for
            // a real user, and its Calor0425s are diagnostics a real user sees.
            //
            // v0.16 K1 replaced schema 2's `if (bindDiagnostics.HasErrors) skip`
            // with the two lines below. That single guard was the whole of the
            // published "8 Calor0425 sites over 99 of 364 modules": it excluded
            // 157 modules the compiler enforces. The raw-bag verdict is still
            // computed — as a NUMBER in the ledger, never as a filter — so the
            // two rules stay legible side by side and the mutation §3.1 K1 names
            // is pinned by an equality instead of by a memo.
            var bindDiagnostics = new DiagnosticBag();
            new Compiler.Binding.Binder(bindDiagnostics).Bind(module);
            var rawBagRejects = bindDiagnostics.HasErrors;
            if (rawBagRejects)
                rawBagBindFailed++;

            var propagatedDiagnostics = new DiagnosticBag();
            Compiler.Binding.BindingDiagnosticPolicy.PropagateCompilationErrors(
                bindDiagnostics, propagatedDiagnostics);
            if (propagatedDiagnostics.HasErrors)
            {
                notMeasured++;
                excludedBindFailed++;
                continue;
            }

            // The 64 MB stack and the catch are belt and braces around ORDINARY
            // exceptions, which do occur. THE CATCH CANNOT CATCH A STACK
            // OVERFLOW (review round 1, F8): in .NET that is a fail-fast — the
            // process dies and no catch block, on any thread, observes it.
            // Schema 2's bind-first guard was what kept the fatal modules away
            // from the pass (measured then: `serilog/src/Serilog/Core/Logger.cs`
            // and `Core/Sinks/Batching/BatchingSink.cs` took the whole test host
            // down, issue #1104), and K1 removes that guard. What replaces it is
            // v0.16 W3(c)'s depth bound in the nested `EffectInferrer`
            // (`EffectEnforcementPass.cs`), pinned by
            // `tests/Calor.Enforcement.Tests/EffectInferrerRecursionTests.cs` —
            // whose existence `K1_IsSequencedAfterW3c_RecursionPinExists` asserts,
            // because a test host that dies takes every other pin with it.
            var faulted = false;
            var worker = new Thread(() =>
            {
                try
                {
                    // Manifests: built-in only. No project directory is passed, so
                    // no `.calor-effects.json` beside the input (or in the cwd) is
                    // ever loaded; the loader is hermetic, so `~/.calor/manifests/`
                    // cannot make the ledger depend on the machine. This is the
                    // in-process twin of N:S2.2's scratch cwd, and it is observed
                    // by K1_ScratchCwdRule_ProjectLocalManifestBesideTheInputIsNotConsulted.
                    new EffectEnforcementPass(effectDiagnostics, resolver: HermeticResolver())
                        .Enforce(module);
                }
                catch
                {
                    faulted = true;
                }
            }, maxStackSize: 64 * 1024 * 1024);
            worker.Start();
            worker.Join();

            if (faulted)
            {
                notMeasured++;
                excludedEffectPassFaulted++;
                continue;
            }

            enforced++;
            if (!rawBagRejects)
                enforcedRawBagRule++;

            // ANTI-VACUITY WITNESS (schema 2): the invocation-bucket Calor0425s
            // counted below. Pre-E4 this was the Calor0418 count, which E4 drove
            // to zero for function-typed values; the SAME invocations now draw
            // the invocation-shaped Calor0425 (no converted module writes a
            // row), so the witness measures the same thing under its new code.
            var rows = effectDiagnostics
                .Where(d => d.Code == DiagnosticCode.EffectRowUnknown)
                .ToList();
            if (rows.Count == 0)
                continue;

            diagnostics += rows.Count;
            modulesWith++;

            foreach (var row in rows)
            {
                // Named per site so a regeneration can be spot-checked by hand
                // (E4's PR did: is Calor0425 the honest code at each one?).
                Console.WriteLine(
                    $"Calor0425-corpus site {name}/{Path.GetRelativePath(srcRoot, file)}"
                    + $"({row.Span.Line},{row.Span.Column}): {row.Message}");

                if (row.Message.StartsWith("Invocation of ", StringComparison.Ordinal))
                {
                    // v0.15 E4 — the three invocation verdicts, by the clause the
                    // message quotes (pinned by full equality in
                    // StrictnessBatchTests.MessageTexts_Calor0425_AtInvocation_*).
                    if (row.Message.Contains("under an assumption", StringComparison.Ordinal))
                        invocationAssumed++;
                    else if (row.Message.Contains("carries no effect row", StringComparison.Ordinal))
                        invocationRowless++;
                    else
                        invocationUndetermined++;
                }
                else if (row.Message.Contains("only under an assumption", StringComparison.Ordinal))
                {
                    assumed++;
                }
                else if (row.Message.Contains("not visible in this module", StringComparison.Ordinal))
                {
                    // E3b's two external-base arms of sites 4/5: the interface arm
                    // (§6.4's third sample, "through a member not visible in this
                    // module") and the override arm ("overrides a member of external
                    // base class 'X', which is not visible in this module").
                    externalBase++;
                }
                else if (row.Message.Contains("with no effect row", StringComparison.Ordinal))
                {
                    rowless++;
                    if (IsInvokedInModule(source, PositionName(row.Message)))
                        rowlessInvoked++;
                    else
                        rowlessNever++;
                }
                else
                {
                    unknownSource++;
                }
            }
        }

        return new SubjectVolume(
            name, diagnostics, modulesWith, enforced, notMeasured,
            rowless, unknownSource, assumed, rowlessInvoked, rowlessNever,
            externalBase, invocationRowless, invocationUndetermined, invocationAssumed,
            invocationRowless + invocationUndetermined + invocationAssumed,
            excludedConversionFailed, excludedParseFailed, excludedBindFailed,
            excludedEffectPassFaulted, enforcedRawBagRule, rawBagBindFailed);
    }

    /// <summary>
    /// v0.16 K1's manifest rule, in one place so the ledger walk and the
    /// scratch-cwd pin cannot drift apart: built-in manifests only. A hermetic
    /// <see cref="Compiler.Effects.Manifests.ManifestLoader"/> keeps
    /// <c>~/.calor/manifests/</c> out (the precedent is
    /// <c>HigherOrderDemandLedgerTests.MeasureDA</c>), and passing no project
    /// directory to <see cref="EffectEnforcementPass"/> keeps
    /// <c>.calor-effects.json</c> beside the input out — the in-process twin of
    /// N:S2.2's scratch cwd.
    /// </summary>
    internal static Compiler.Effects.EffectResolver HermeticResolver()
    {
        var resolver = new Compiler.Effects.EffectResolver(
            new Compiler.Effects.Manifests.ManifestLoader(loadUserLevelManifests: false));
        resolver.Initialize(projectDirectory: null, solutionDirectory: null);
        return resolver;
    }

    /// <summary>
    /// The quoted position name out of §6.4's second message sample —
    /// <c>Parameter 'transform' of 'Apply' is function-typed…</c> yields
    /// <c>transform</c>. Empty when the message shape changes, which makes the
    /// fourth split fall to "never invoked" rather than throw; the message
    /// itself is pinned by P22, so a change there is caught before it reaches
    /// here.
    /// </summary>
    private static string PositionName(string message)
    {
        var open = message.IndexOf('\'');
        if (open < 0) return string.Empty;
        var close = message.IndexOf('\'', open + 1);
        return close < 0 ? string.Empty : message[(open + 1)..close];
    }

    /// <summary>
    /// §13.4's fourth split: is the row-less position ever INVOKED, or only
    /// declared? A textual probe over the converted module, and deliberately a
    /// crude one — the alternative is a second AST walk that would answer the
    /// same question with more machinery and the same caveat, since a delegate
    /// reached through a field chain is invisible to both.
    /// </summary>
    private static bool IsInvokedInModule(string source, string name) =>
        name.Length > 0
        && (source.Contains($"§C{{{name}}}", StringComparison.Ordinal)
            || source.Contains($"§C{{{name}.", StringComparison.Ordinal));

    private sealed record SubjectVolume(
        string Subject,
        int Diagnostics,
        int ModulesWithDiagnostics,
        int ModulesEnforced,
        int ModulesNotMeasured,
        int RowlessDestination,
        int UnknownSource,
        int Assumed,
        int RowlessInvoked,
        int RowlessNeverInvoked,
        /// <summary>v0.15 E4 (schema 2) — §13.4's widened split; see the class
        /// comment. <c>InvocationWitness</c> is the sum of the three invocation
        /// buckets and replaces schema 1's <c>Calor0418Witness</c>.</summary>
        int ExternalBase,
        int InvocationRowless,
        int InvocationUndetermined,
        int InvocationAssumed,
        int InvocationWitness,
        /// <summary>F7 — the exclusion-reason histogram. With schema 3's
        /// <c>ExcludedEffectPassFaulted</c> these FOUR sum to
        /// <c>ModulesNotMeasured</c>. <c>ExcludedBindFailed</c> now counts the
        /// modules that stop at a PROPAGATED binding error — what a user sees —
        /// not the ones the raw binder bag merely disliked.</summary>
        int ExcludedConversionFailed,
        int ExcludedParseFailed,
        int ExcludedBindFailed,
        /// <summary>v0.16 K1 (schema 3). Ordinary exceptions out of the effect
        /// pass, split out of <c>ExcludedBindFailed</c> now that the production
        /// rule hands the pass ~157 modules schema 2 never gave it.</summary>
        int ExcludedEffectPassFaulted,
        /// <summary>v0.16 K1 (schema 3) — §3.1 K1's discriminating pin as a
        /// number. How many modules schema 2's raw-bag guard would have enforced.
        /// Restoring that guard makes <c>ModulesEnforced</c> equal this, and the
        /// per-subject equality above goes red.</summary>
        int ModulesEnforcedRawBagRule,
        /// <summary>v0.16 K1 (schema 3). Modules that parsed but whose RAW binder
        /// bag has errors — schema 2's exclusion count, kept so the size of the
        /// correction is readable from the ledger alone.</summary>
        int RawBagBindFailed);

    /// <summary>v0.16 K1 — one per-subject leg of gate 9's <c>ModulesEnforced</c>
    /// floor.</summary>
    private sealed record SubjectFloor(string Subject, int ModulesEnforcedMin);

    /// <summary>
    /// v0.16 §5 gate 9's floor rule, written into the ledger by K1's PR. See
    /// <see cref="RegisteredFloorRule"/> for why the two legs are at different
    /// stages and why that is stated rather than smoothed over.
    /// </summary>
    private sealed record Gate9FloorRule(
        string Gate,
        string BindRule,
        int ModulesEnforcedMin,
        List<SubjectFloor> PerSubjectModulesEnforcedMin,
        int ExcludedParseFailedMax,
        /// <summary>The value observed when the floor was registered (K1's PR).
        /// Pinned EXACTLY while <c>ExcludedParseFailedPendingUntil</c> is set, so
        /// the item it waits on is what flips the rule.</summary>
        int ExcludedParseFailedRegisteredAt,
        /// <summary>The roadmap item that must merge before
        /// <c>ExcludedParseFailedMax</c> can be met; empty once it is met.</summary>
        string ExcludedParseFailedPendingUntil,
        string Note);

    private sealed record Ledger(
        int SchemaVersion,
        string Scope,
        /// <summary>v0.16 K1 — <c>"propagated"</c>: the binder's bag is filtered
        /// through <c>BindingDiagnosticPolicy.PropagateCompilationErrors</c>
        /// before the stop, as <c>Program.cs:820/829-833</c> does. The Calor0270
        /// ledger carries <c>"parsed"</c> for its own rule.</summary>
        string BindRule,
        /// <summary>v0.16 §5 gate 9's floor, registered with the numbers it
        /// gates.</summary>
        Gate9FloorRule FloorRule,
        string MeasuredCommit,
        int AggregateDiagnostics,
        int AggregateModulesWithDiagnostics,
        int AggregateModulesEnforced,
        /// <summary>Review round 1 (F7). Modules that never reached the effect
        /// pass — conversion threw, produced nothing, failed to parse, stopped at
        /// a propagated binding error, or faulted in the pass. Under schema 2's
        /// raw-bag guard this was 73% of the corpus; under the production rule
        /// (schema 3) it is the compiler's own reach.</summary>
        int AggregateModulesExcluded,
        List<SubjectVolume> PerSubject);
}
