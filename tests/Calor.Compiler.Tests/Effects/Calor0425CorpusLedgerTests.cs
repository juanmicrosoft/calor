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
/// <para><b>Manifest rule.</b> The pass is constructed with no project directory,
/// so the only channel for a project-local <c>.calor-effects.json</c> — the
/// INPUT FILE's own directory (<c>Program.cs:488</c>/<c>:509</c>, not the cwd) —
/// is closed, and with a hermetic
/// <see cref="Calor.Compiler.Effects.Manifests.ManifestLoader"/> so
/// <c>~/.calor/manifests/</c> cannot make the number depend on the machine. That
/// is deliberately STRICTER than the CLI leg, which does read the user-level
/// directory. <c>K1_ManifestRule_TheLedgerReadsBuiltInManifestsOnly</c> observes
/// all three legs, and
/// <c>K1_CrossCheckInvariant_NoProjectLocalManifestBesideAnyMeasuredModule</c>
/// pins the repository-side invariant the CLI leg depends on.</para>
///
/// <para><b>The CLI cross-check travels with the numbers.</b> §S2 requires the
/// in-process and pinned-CLI measurements to agree per subject on every row.
/// That result is recorded in the ledger's <c>CliCrossCheck</c> block and
/// asserted against the in-process cells by
/// <c>Gate9_CliCrossCheck_AgreesWithTheInProcessMeasurement</c>, so a
/// regeneration that moves the in-process numbers without re-running the CLI leg
/// fails loudly instead of silently dropping the second measurement.</para>
/// </summary>
public class Calor0425CorpusLedgerTests
{
    // v0.16 K1: schema 3 = schema 2 + BindRule + FloorRule + the raw-bag
    // denominator kept beside the production one + the exclusion split that the
    // widened denominator made worth separating (effect-pass faults).
    // v0.17 R1: schema 4 = schema 3 + the bind-failure histogram and its module
    // list (roadmap-v0.17 §3.1 R1, gate 13) + the Calor0411 count over the
    // enforced set, which the IL-rows trigger was being read without.
    private const int SchemaVersion = 4;

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
        + "excluded from the denominator and counted separately. SCHEMA 4 (v0.17 R1) adds two "
        + "things its numbers cannot be read without. BindFailureCauses/BindFailureModules break "
        + "the propagated bind stops out by the FIRST propagated error — the one the compiler "
        + "stops on — and BindFailureMultiCause counts the modules carrying more than one, "
        + "because a module attributed to the largest cluster that also stops elsewhere cannot "
        + "be recovered by fixing that cluster alone. Calor0411Sites/Calor0411Modules count "
        + "UnknownExternalCall over the SAME enforced set, because the unresolved-receiver class "
        + "never reaches Calor0425 — the bare-target guard sends it through the unknown-call "
        + "chain — so a trigger reading UnknownSource + InvocationUndetermined was being read "
        + "against a partial denominator. Calor0411 is EVERY unknown external call, not only the "
        + "delegate-returning ones that trigger concerns, so it is an UPPER BOUND on that demand "
        + "and not a measure of it. Manifests are BUILT-IN ONLY — "
        + "deliberately hermetic, and therefore STRICTER than the CLI leg, which still reads "
        + "~/.calor/manifests: the pass is constructed with no project directory (so no "
        + ".calor-effects.json beside the input can be read, which is the channel the CLI leg "
        + "closes by keeping its converted-module directory manifest-free) and the loader is "
        + "built with loadUserLevelManifests:false (so the machine cannot move the number). The "
        + "invoked / never-invoked split of the row-less destinations is a TEXTUAL probe over the "
        + "converted module covering §C{name} calls and the (?. name \"Invoke\") interop form; a "
        + "delegate reached through a field chain or an alias is invisible to it, so "
        + "RowlessNeverInvoked is an UPPER bound and RowlessInvoked a lower one. Causes are the "
        + "ones the FIVE MONOMORPHIC SITES of "
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
        0,
        "",
        99,
        "ModulesEnforced >= 250 is a live regression floor (six below the 256 observed at "
        + "registration; the per-subject MediatR/serilog floors are EXACT and the slack sits in "
        + "FluentValidation). ExcludedParseFailed <= 2 is the registered user-visible bar. It was "
        + "NOT met at K1's registration — 59 modules failed to parse, pinned EXACTLY at "
        + "ExcludedParseFailedRegisteredAt with ExcludedParseFailedPendingUntil = \"W3(a)\" so "
        + "that W3(a)'s merge would flip this rule rather than silently satisfy a weakened one. "
        + "v0.16 W3(a) (#903 clusters 1-2, PR #1125) then recovered ALL 59, not the 57 it was "
        + "registered for: clusters 1 and 2 account for 57 and cluster 3 (Calor0117, two modules) "
        + "turned out to be trivial and landed with them. The observed value is now 0, at or "
        + "under the bar, so the pending state is cleared and RegisteredAt is restated at the "
        + "value the gate now holds EXACTLY — the leg is live rather than pending, and a "
        + "regression to even one parse failure reds it.");

    /// <summary>
    /// v0.16 K1, §S2's registration-time cross-check, recorded so it cannot go
    /// missing. The in-process measurement above and the pinned CLI pass must
    /// agree per subject on every row; §2.2 requires the CLI leg to be re-run
    /// whenever the ledger is regenerated. Those CLI counts are a MEASUREMENT,
    /// not a recomputation — no test can re-derive them without spawning 364
    /// processes — so they are registered here and
    /// <see cref="Gate9_CliCrossCheck_AgreesWithTheInProcessMeasurement"/> holds
    /// them against the in-process cells. A regeneration that moves an
    /// in-process number without re-running the CLI leg therefore FAILS instead
    /// of quietly dropping the second measurement.
    ///
    /// <para>Note the places the two rules differ in DETAIL while agreeing on the
    /// outcome. First, the first-code histogram: <c>BindValidationPass</c>
    /// (<c>Program.cs:790</c>) reports Calor0250 ahead of the binder in the CLI,
    /// so the two legs bucket the same modules under different codes; the SETS
    /// coincide, which is why the per-subject equalities below hold and why the
    /// histogram is recorded in prose rather than pinned as a number. Second — new
    /// with W3(a) — three of the newly-recovered FluentValidation modules stop in
    /// the CLI at Calor0209 (IllegalYield, <c>ReturnValidationPass</c>,
    /// <c>Program.cs:799</c>), a pass the in-process leg does not run at all. They
    /// are counted in <c>ReachEffectPass</c> so the per-subject equality with the
    /// in-process denominator still holds, and separately in
    /// <c>StoppedInCliOnlyPass</c> so the outcome buckets still add up. This is
    /// the roadmap's documented CLI-only-pass divergence (§5 gate 9's
    /// NOT-ADJUDICATED route names <c>ReturnValidationPass</c> explicitly),
    /// recorded rather than smoothed away.</para>
    /// </summary>

    /// <summary>
    /// v0.17 R2 — the CLI leg, MEASURED instead of hand-registered.
    /// <para>Gate 9's cross-check exists so a regeneration that moves the
    /// in-process numbers cannot silently drop the second measurement. It was
    /// enforced against a constant someone had to remember to update, which is
    /// the same shape of trap as a hand-maintained equality list: R2 moved the
    /// in-process denominator and the constant went stale immediately. It is
    /// now re-derived on every regeneration from the SAME conversion the
    /// in-process leg uses — so the two legs cannot drift by neglect, only by a
    /// real disagreement, which is what the gate is for.</para>
    /// <para>Outside a regeneration the committed block is returned unchanged,
    /// so an ordinary test run stays fast and still checks the committed bytes.
    /// </para>
    /// </summary>
    private static CliCrossCheck MeasureCliCrossCheck(
        string root,
        List<SubjectVolume> inProcess)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CALOR_REGENERATE_CALOR0425_LEDGER"),
                "1", StringComparison.Ordinal))
        {
            return CommittedLedger().CliCrossCheck;
        }

        var rows = new List<CliCrossCheckSubject>();
        foreach (var subject in inProcess)
        {
            var measured = MeasureCliSubject(root, subject.Subject);
            rows.Add(measured);
            Console.WriteLine(
                $"Calor0425-corpus CLI leg {measured.Subject}: files {measured.Files}, "
                + $"parse {measured.ParseFailed}, bind {measured.BindStopped}, "
                + $"reach {measured.ReachEffectPass}, 0425 {measured.Calor0425Sites} sites over "
                + $"{measured.Calor0425Modules} modules");
        }

        return new CliCrossCheck(
            "the in-process conversion of this ledger (Lossy / SelectActiveBranchLossy, empty "
            + "defined symbols), each module written to a scratch directory holding no "
            + ".calor-effects.json, then `dotnet calor.dll -i <module>.calr -o <scratch>/out.g.cs` "
            + "— no flags, one process per module",
            "v0.17 R2: re-derived automatically on every ledger regeneration rather than "
            + "hand-registered, so the two legs cannot drift by neglect",
            rows);
    }

    /// <summary>
    /// v0.17 R2 — one subject's CLI leg. The conversion is the ledger's own, so
    /// the two legs differ ONLY in what runs after it: in-process
    /// <c>EffectEnforcementPass</c> versus the shipping CLI, which additionally
    /// runs the documented CLI-only passes (<c>Program.cs:760-808</c>).
    /// </summary>
    private static CliCrossCheckSubject MeasureCliSubject(string root, string subject)
    {
        var srcRoot = Path.Combine(root, "bench", "corpus", subject, "src");
        var cli = Path.Combine(root, "src", "Calor.Compiler", "bin", "Debug", "net10.0", "calor.dll");
        var files = Directory
            .EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToList();

        var scratch = Path.Combine(Path.GetTempPath(), "calor-cli-cross-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        int parseFailed = 0, bindStopped = 0, reach = 0, stop0410 = 0, stop042X = 0;
        int stop1002 = 0, clean = 0, cliOnly = 0, modules0425 = 0, sites0425 = 0;
        try
        {
            foreach (var file in files)
            {
                var calor = ConvertForLedger(file);
                if (calor == null)
                {
                    parseFailed++;
                    continue;
                }

                var modulePath = Path.Combine(scratch, "m.calr");
                File.WriteAllText(modulePath, calor);
                var output = RunCli(cli, scratch);

                var sites = CountOccurrences(output, "Calor0425");
                if (sites > 0)
                {
                    modules0425++;
                    sites0425 += sites;
                }

                if (output.Contains("Calor0099", StringComparison.Ordinal)
                    || output.Contains("Calor0100", StringComparison.Ordinal)
                    || output.Contains("Calor0117", StringComparison.Ordinal))
                {
                    parseFailed++;
                    continue;
                }

                if (output.Contains("Calor0208", StringComparison.Ordinal)
                    || output.Contains("Calor0250", StringComparison.Ordinal)
                    || output.Contains("Calor0201", StringComparison.Ordinal))
                {
                    bindStopped++;
                    continue;
                }

                reach++;
                if (output.Contains("error Calor0410", StringComparison.Ordinal))
                    stop0410++;
                else if (output.Contains("error Calor0422", StringComparison.Ordinal)
                    || output.Contains("error Calor0423", StringComparison.Ordinal))
                    stop042X++;
                else if (output.Contains("error Calor1002", StringComparison.Ordinal))
                    stop1002++;
                else if (output.Contains("error Calor", StringComparison.Ordinal))
                    cliOnly++;
                else
                    clean++;
            }
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); } catch { /* scratch */ }
        }

        return new CliCrossCheckSubject(subject, files.Count, parseFailed, bindStopped, reach,
            stop0410, stop042X, stop1002, clean, cliOnly, modules0425, sites0425);
    }

    /// <summary>The ledger's own conversion, so both legs share one input.</summary>
    private static string? ConvertForLedger(string file)
    {
        try
        {
            var conversion = new Compiler.Migration.CSharpToCalorConverter(
                new Compiler.Migration.ConversionOptions
                {
                    Fidelity = Compiler.Migration.ConversionFidelity.Lossy,
                    PreprocessorMode = Compiler.Migration.PreprocessorConversionMode
                        .SelectActiveBranchLossy,
                    DefinedSymbols = Array.Empty<string>(),
                    ModuleName = "Calor0425Leg",
                    GracefulFallback = true,
                    AutoGenerateIds = true
                }).Convert(File.ReadAllText(file), Path.GetFileName(file));
            return string.IsNullOrEmpty(conversion.CalorSource)
                ? null
                : conversion.CalorSource.Replace("\r\n", "\n");
        }
        catch
        {
            return null;
        }
    }

    private static string RunCli(string cliDll, string workingDirectory)
    {
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(cliDll);
        start.ArgumentList.Add("-i");
        start.ArgumentList.Add("m.calr");
        start.ArgumentList.Add("-o");
        start.ArgumentList.Add("out.g.cs");
        start.Environment["LC_ALL"] = "C";
        using var process = System.Diagnostics.Process.Start(start);
        Assert.NotNull(process);
        var stdout = process!.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return stdout.Result + stderr.Result;
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

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
            MeasureCliCrossCheck(root, perSubject),
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
        // v0.17 R2 — the CLI leg is MEASURED on every regeneration, not compared
        // to a hand-registered constant. That constant was the thing that went
        // stale the moment R2 moved the in-process denominator, and keeping it
        // would mean maintaining the same numbers in two places. What guards the
        // block now is `Gate9_CliCrossCheck_AgreesWithTheInProcessMeasurement`,
        // which holds it against the in-process cells it must agree with —
        // a real cross-check rather than a copy of itself.

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
                // The record's synthesized printer renders schema 4's two collection
                // members as `SortedDictionary`2[...]` / `List`1[System.String]`, so a
                // run where a module's stop code merely SHIFTS (Calor0208 -> Calor0250,
                // counts still summing to the same ExcludedBindFailed) would print two
                // character-identical lines above and fire with no indication of what
                // moved — for exactly the fields schema 4 added. Spelled out here
                // instead of overriding PrintMembers, which on a sealed record would
                // mean re-printing all 25 members by hand.
                + $"  causes   : {Describe(expected.BindFailureCauses)}"
                + $"  ->  {Describe(actual.BindFailureCauses)}\n"
                + $"  modules  : {DescribeDelta(expected.BindFailureModules, actual.BindFailureModules)}\n"
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
    /// Gate 9's <c>ExcludedParseFailed</c> leg. K1 registered it PENDING: the bar
    /// is <c>≤ 2</c>, the value at registration was 59, and the current value was
    /// pinned EXACTLY with <c>PendingUntil = "W3(a)"</c> so that W3(a)'s merge
    /// would turn this red and force the flip rather than silently satisfying a
    /// weakened rule. <b>That flip has now happened</b> (W3(a) = #903 clusters
    /// 1–2, PR #1125, which also carried cluster 3): the observed value is 0, so
    /// <c>PendingUntil</c> is cleared and <c>RegisteredAt</c> restated at 0. The
    /// leg is now LIVE — the same exact-equality assertion reds on any regression
    /// to even one parse failure — and the second assertion is the consistency
    /// that stops a pending state outliving the thing it waited on. The method
    /// name is kept as K1 wrote it so its cross-references still resolve.
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
    /// The <b>backstop</b> for §3.1 K1's discriminating pin. The pin itself lives
    /// in <see cref="Calor0425CorpusLedgerMatchesRecomputation"/>: restoring
    /// schema 2's raw-bag guard makes the live measurement report 99 and that
    /// test reds (measured — the failure message names the 99). This test reads
    /// only the COMMITTED JSON, so a mutated guard alone does not red it; what it
    /// catches is the ledger being regenerated WRONG — a committed file whose
    /// production denominator has collapsed onto the raw-bag one, which is what a
    /// mutation plus a regeneration would leave behind.
    /// </summary>
    [Fact]
    public void Gate9_BothBindRules_AreRecorded_AndTheProductionOneIsWider()
    {
        var committed = CommittedLedger();
        var production = committed.AggregateModulesEnforced;
        var rawBag = committed.PerSubject.Sum(s => s.ModulesEnforcedRawBagRule);

        Assert.True(rawBag > 0, "the raw-bag denominator is not recorded");

        // >= is the structural fact (the propagated filter only ever REMOVES
        // diagnostics, so it accepts every module the raw bag accepts). The
        // discriminating statement is the second one: a converter fix could
        // legitimately make the two denominators converge, but it cannot make the
        // production rule land back on the raw-bag denominator REGISTERED at K1 —
        // that is what restoring the guard produces.
        Assert.True(production >= rawBag,
            $"The production rule enforces {production} modules and the raw-bag rule "
            + $"{rawBag}. The production rule can only be WIDER — every module the raw bag "
            + "accepts, the propagated filter accepts too, so this is impossible without a "
            + "hand-edited ledger.");
        Assert.True(production != committed.FloorRule.RawBagDenominatorAtRegistration,
            $"The production denominator is {production}, exactly the raw-bag denominator "
            + $"registered at K1 ({committed.FloorRule.RawBagDenominatorAtRegistration}). That is "
            + "what restoring schema 2's raw-bag guard produces (roadmap §3.1 K1's "
            + "discriminating pin: ModulesEnforced 256 → 99 → red).");

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
    /// §S2's requirement, as a standing test: the in-process measurement and the
    /// pinned CLI pass agree per subject on <b>every</b> row. The CLI numbers are
    /// a registered measurement (364 processes — nothing here can re-derive
    /// them), so this holds them against the in-process cells the ledger
    /// recomputes. A regeneration that moves an in-process number without
    /// re-running the CLI leg goes RED, which is the whole point: the second
    /// measurement cannot be silently dropped.
    /// </summary>
    [Fact]
    public void Gate9_CliCrossCheck_AgreesWithTheInProcessMeasurement()
    {
        var committed = CommittedLedger();
        var cli = committed.CliCrossCheck;

        Assert.Equal(
            committed.PerSubject.Select(s => s.Subject),
            cli.PerSubject.Select(s => s.Subject));

        foreach (var (inProcess, cliRow) in committed.PerSubject.Zip(cli.PerSubject))
        {
            var why = $"S2 cross-check disagreement for {inProcess.Subject} — the in-process "
                + "measurement moved and the CLI leg was not re-run. Re-run the pinned CLI pass "
                + "over the converted modules and update CliCrossCheck in the same PR "
                + "(roadmap-v0.16.md §2.2: \"the in-process and CLI rows must agree per subject "
                + "as they do today\"). ";

            Assert.True(cliRow.ReachEffectPass == inProcess.ModulesEnforced,
                why + $"reach {cliRow.ReachEffectPass} vs enforced {inProcess.ModulesEnforced}");
            Assert.True(cliRow.ParseFailed == inProcess.ExcludedParseFailed,
                why + $"parse {cliRow.ParseFailed} vs {inProcess.ExcludedParseFailed}");
            Assert.True(cliRow.BindStopped == inProcess.ExcludedBindFailed,
                why + $"bind {cliRow.BindStopped} vs {inProcess.ExcludedBindFailed}");
            Assert.True(cliRow.Calor0425Modules == inProcess.ModulesWithDiagnostics,
                why + $"0425 modules {cliRow.Calor0425Modules} vs "
                + $"{inProcess.ModulesWithDiagnostics}");
            Assert.True(cliRow.Calor0425Sites == inProcess.Diagnostics,
                why + $"0425 sites {cliRow.Calor0425Sites} vs {inProcess.Diagnostics}");
            Assert.True(cliRow.Files == inProcess.ModulesEnforced + inProcess.ModulesNotMeasured,
                why + $"files {cliRow.Files} vs "
                + $"{inProcess.ModulesEnforced + inProcess.ModulesNotMeasured}");

            // The CLI row's own arithmetic: every module that reaches the pass
            // lands in exactly one outcome bucket — or is stopped first by one of
            // the documented CLI-only passes, which the in-process leg does not
            // run (see StoppedInCliOnlyPass).
            Assert.Equal(
                cliRow.ReachEffectPass,
                cliRow.StopCalor0410 + cliRow.StopCalor0422Or0423 + cliRow.StopCalor1002
                    + cliRow.CompileClean + cliRow.StoppedInCliOnlyPass);
            Assert.Equal(
                cliRow.Files,
                cliRow.ParseFailed + cliRow.BindStopped + cliRow.ReachEffectPass);
        }

        Assert.Equal(364, cli.PerSubject.Sum(s => s.Files));

        // The aggregate is AGREEMENT plus a regression floor, not a frozen
        // number. 256 at K1's registration; 304 after W3(a) recovered all 59
        // parse failures; 319 after v0.17 R2 taught overload resolution
        // assignability. Pinning the literal made this gate fail every time the
        // measurement legitimately improved — the same moving-target trap
        // review round 3 caught in PP-R1's effect size, in the opposite
        // direction.
        Assert.Equal(
            committed.AggregateModulesEnforced,
            cli.PerSubject.Sum(s => s.ReachEffectPass));
        Assert.True(cli.PerSubject.Sum(s => s.ReachEffectPass) >= 304,
            "gate 9's regression floor: the CLI leg reached "
            + $"{cli.PerSubject.Sum(s => s.ReachEffectPass)} modules, below the 304 W3(a) "
            + "established.");
        Assert.Equal(0, cli.PerSubject.Sum(s => s.ParseFailed));
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
    /// <b>K1's manifest rule</b> — what actually keeps the in-process leg and
    /// N:S2.2's CLI leg reading the same manifests.
    ///
    /// <para><b>The cwd is not the channel, so this pin does not guard it.</b>
    /// An earlier draft of this test set the process cwd to a directory holding a
    /// <c>.calor-effects.json</c> and asserted the ledger ignored it. That leg
    /// could not fail: <c>ManifestLoader.LoadAll</c> only reaches
    /// <c>LoadProjectLocalManifest</c> when a project directory is passed, and the
    /// CLI derives that directory from the INPUT FILE
    /// (<c>Program.cs:488</c>/<c>:509</c>, <c>Path.GetDirectoryName(file.FullName)</c>),
    /// never from the cwd. The real channel is the directory the module being
    /// compiled sits in, and that is what the legs below pin.</para>
    ///
    /// <para>(1) <b>Non-vacuity</b> — the manifest is load-bearing: hand its
    /// directory in as the project directory and the module's Calor0411
    /// disappears. (2) <b>The channel, spelled as the CLI spells it</b> — the
    /// directory <c>Path.GetDirectoryName(inputPath)</c> yields IS read, while the
    /// ledger's own construction (<c>projectDirectory: null</c>) is not.
    /// (3) <b>The loader</b> the ledger uses carries built-in manifests only.</para>
    ///
    /// <para>Hermetic is deliberately STRICTER than the CLI leg, which still
    /// reads <c>~/.calor/manifests/</c>: the in-process number cannot depend on
    /// the machine, and the CLI leg's agreement with it is an empirical result
    /// recorded in <c>CliCrossCheck</c>, not an assumption.</para>
    /// </summary>
    [Fact]
    public void K1_ManifestRule_TheLedgerReadsBuiltInManifestsOnly()
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

        var scratch = Path.Combine(Path.GetTempPath(), "calor-k1-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            File.WriteAllText(Path.Combine(scratch, ".calor-effects.json"), """
                {
                  "version": "1.0",
                  "description": "K1 manifest-rule pin — must never be consulted by the ledger",
                  "mappings": [
                    { "type": "K1ScratchWidget", "methods": { "Ping": ["cw"] } }
                  ]
                }
                """);

            // (1) Load-bearing.
            var withManifest = EnforceOnce(source, projectDirectory: scratch, hermetic: false);
            Assert.DoesNotContain(withManifest, d => d.Code == DiagnosticCode.UnknownExternalCall);

            // (2) The channel is the INPUT FILE's directory, derived here exactly
            //     as Program.cs:509 derives it — and the ledger's construction,
            //     which passes no project directory, does not open it.
            var inputPath = Path.Combine(scratch, "module.calr");
            File.WriteAllText(inputPath, source);
            var asTheCliDerivesIt = EnforceOnce(
                source, projectDirectory: Path.GetDirectoryName(inputPath), hermetic: false);
            Assert.DoesNotContain(asTheCliDerivesIt, d => d.Code == DiagnosticCode.UnknownExternalCall);

            var asTheLedgerRunsIt = EnforceOnce(source, projectDirectory: null, hermetic: true);
            Assert.Contains(asTheLedgerRunsIt, d => d.Code == DiagnosticCode.UnknownExternalCall);
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
        }

        // (3) …and the loader behind it carries built-in manifests only.
        var loader = new Compiler.Effects.Manifests.ManifestLoader(loadUserLevelManifests: false);
        loader.LoadAll(projectDirectory: null, solutionDirectory: null);
        Assert.NotEmpty(loader.LoadedManifests);
        Assert.All(loader.LoadedManifests, entry => Assert.Equal(
            Compiler.Effects.Manifests.ManifestPriority.BuiltIn, entry.Source.Priority));
    }

    /// <summary>
    /// The invariant the CLI leg of the S2 cross-check depends on, which nothing
    /// pinned before: a converted module handed to the CLI must not have a
    /// <c>.calor-effects.json</c> as a SIBLING, because <c>Program.cs:509</c>
    /// makes that file's directory the project directory and the manifest would
    /// silently resolve calls the in-process leg leaves Unknown. The cross-check
    /// dumps into a scratch directory, so the durable half of the invariant is
    /// the repository side: the three corpus subject <c>src/</c> trees the
    /// modules are converted FROM, and <c>bench/phase0-agent-native/</c>, where
    /// the ledgers live and where any in-repo dump would land.
    ///
    /// <para><b>Deliberately NOT the whole of <c>bench/</c>.</b>
    /// <c>bench/corpus/manifests/</c> holds six package manifests
    /// (<c>MediatR.Contracts</c>, <c>System.Threading.Channels</c>, …) on purpose;
    /// they are not siblings of any converted module, so no compilation derives a
    /// project directory that reaches them, and the CLI leg never loaded one. A
    /// sweep over all of <c>bench/</c> flags those and says nothing true — the
    /// scope below is the set of directories where such a file would actually
    /// change a measurement.</para>
    /// </summary>
    [Fact]
    public void K1_CrossCheckInvariant_NoProjectLocalManifestBesideAnyMeasuredModule()
    {
        var root = RepoRoot();
        var roots = new List<string> { Path.Combine(root, "bench", "phase0-agent-native") };
        roots.AddRange(Subjects.Select(s => Path.Combine(root, "bench", "corpus", s, "src")));

        foreach (var directory in roots.Where(Directory.Exists))
        {
            var manifests = Directory
                .EnumerateFiles(directory, "*.calor-effects.json", SearchOption.AllDirectories)
                .ToList();
            Assert.True(manifests.Count == 0,
                $"A project-local effect manifest sits under {directory}: "
                + string.Join(", ", manifests)
                + ". Program.cs:509 makes an input file's own directory the project directory, so "
                + "such a file would be read for any module beside it — the CLI leg of the S2 "
                + "cross-check would then resolve calls the in-process leg (which passes no "
                + "project directory) leaves Unknown, and the two legs would stop measuring the "
                + "same compiler.");
        }
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
        Assert.Equal(
            expected.RawBagDenominatorAtRegistration, actual.RawBagDenominatorAtRegistration);
        Assert.Equal(expected.Note, actual.Note);
    }

    private static void AssertCliCrossCheckEqual(CliCrossCheck expected, CliCrossCheck actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Invocation, actual.Invocation);
        Assert.Equal(expected.MeasuredAt, actual.MeasuredAt);
        Assert.Equal(expected.PerSubject, actual.PerSubject);
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
        // v0.17 R1 (schema 4): the bind-failure histogram, the modules behind it,
        // and the Calor0411 denominator. See the sites where each is filled.
        var bindFailureCauses = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var bindFailureModules = new List<string>();
        int calor0411Sites = 0, calor0411Modules = 0, bindFailureMultiCause = 0;
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

                // v0.17 R1 (schema 4). The 60 modules that stop here were a COUNT
                // and nothing else, in any committed ledger, which is why
                // roadmap-v0.17 §3.1 R2 cannot be scoped without this: R2 fixes
                // "the largest cluster R1 names", and until now nothing named one.
                // The cause is the FIRST propagated error, because that is the one
                // the shipping compiler stops on (`Program.cs:829-833`) and so the
                // only one a user sees; counting every error in the bag would
                // weight a module by how many follow-on errors it happened to
                // cascade into.
                var stopCode = propagatedDiagnostics.Errors[0].Code.ToString();
                bindFailureCauses[stopCode] = bindFailureCauses.GetValueOrDefault(stopCode) + 1;

                // A module can carry MORE THAN ONE propagated code, and this
                // attributes it to the FIRST — which is the binder's REPORT order
                // (symbol registration before body binding), not source order. That
                // matters to §4.1's frozen effect size: R2's target of 20 is half of
                // the Calor0208 cluster of 40, and a module in that cluster that ALSO
                // stops on Calor0250 cannot be recovered by fixing Calor0208 alone. So
                // the ambiguity is published rather than hidden — if this count is
                // large, the cluster is softer than its number suggests, and PP-R1's
                // route for that is a MISS with the cause named, not a re-scoping.
                if (propagatedDiagnostics.Errors.Select(d => d.Code).Distinct().Count() > 1)
                    bindFailureMultiCause++;

                // Forward slashes ALWAYS: this is the first path to enter the pinned
                // equality, and every sibling ledger normalizes for exactly this reason
                // (HigherOrderDemandLedgerTests.cs:226, EffectResolverKeyLedgerTests.cs:259,
                // BinderIncompleteRatchetTests.cs:94). Without it a Windows run measures
                // `MediatR\Mediator.cs`, SequenceEqual fails, and the recomputation pin
                // reds with "volume moved" when nothing did — or a Windows regeneration
                // rewrites the ledger and flips the failure onto Linux CI.
                bindFailureModules.Add(
                    $"{stopCode} {Path.GetRelativePath(srcRoot, file).Replace('\\', '/')}");
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

            // v0.17 R1 (schema 4) — the denominator the IL-rows trigger was being
            // read WITHOUT. This ledger's own class comment records that §13.4's
            // "unresolved receiver" never reaches Calor0425: the bare-target guard
            // sends it out as Calor0411 through the unknown-call chain. No
            // committed ledger counted that, so "UnknownSource +
            // InvocationUndetermined = 0" was a statement about the sites that
            // reach THIS code, with an adjacent class unmeasured by construction
            // (roadmap-v0.17 §0.3, finding M1). Counted here over exactly the
            // enforced set, so the two are readable against one denominator.
            var unknownCalls = effectDiagnostics
                .Count(d => d.Code == DiagnosticCode.UnknownExternalCall);
            if (unknownCalls > 0)
            {
                calor0411Sites += unknownCalls;
                calor0411Modules++;
            }

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
            excludedEffectPassFaulted, enforcedRawBagRule, rawBagBindFailed,
            bindFailureCauses, bindFailureModules, bindFailureMultiCause,
            calor0411Sites, calor0411Modules);
    }

    private static string Describe(SortedDictionary<string, int>? causes) =>
        causes is null ? "<null>" : string.Join("+", causes.Select(c => $"{c.Key}:{c.Value}"));

    /// <summary>
    /// What moved, described the way the assertion COMPARES. The equality is
    /// <see cref="Enumerable.SequenceEqual{T}(IEnumerable{T},IEnumerable{T})"/>,
    /// which is order-sensitive; a set difference is not. Same modules in a
    /// different order — a platform sort-order change, or a corpus rename that
    /// only shifts positions — would fail the pin while this printed
    /// "identical", telling the reader nothing moved. So an order-only
    /// difference reports the first index where the two disagree.
    /// </summary>
    private static string DescribeDelta(List<string>? committed, List<string>? measured)
    {
        if (committed is null || measured is null)
            return "<null>";
        var gone = committed.Except(measured, StringComparer.Ordinal).ToList();
        var came = measured.Except(committed, StringComparer.Ordinal).ToList();
        if (gone.Count > 0 || came.Count > 0)
            return $"-[{string.Join("; ", gone)}] +[{string.Join("; ", came)}]";
        if (committed.SequenceEqual(measured, StringComparer.Ordinal))
            return "identical";
        var i = Enumerable.Range(0, Math.Min(committed.Count, measured.Count))
            .First(n => !string.Equals(committed[n], measured[n], StringComparison.Ordinal));
        return $"same members, ORDER differs — first at index {i}: "
            + $"committed '{committed[i]}' vs measured '{measured[i]}'";
    }

    /// <summary>
    /// System.Text.Json leaves schema 4's members null for a schema-3 file, and
    /// the only schema assertion lives in the recomputation test, which SKIPS on
    /// a bare clone. Without this a stale ledger (a bad merge, a
    /// <c>git checkout main -- ...</c>) makes the gate tests throw
    /// NullReferenceException instead of naming the mismatch.
    /// </summary>
    private static void AssertSchema4(Ledger committed)
    {
        Assert.True(committed.SchemaVersion >= 4,
            $"the committed ledger is schema {committed.SchemaVersion}; these gates read schema 4 "
            + "fields (BindFailureCauses, BindFailureModules, Calor0411*). Regenerate with "
            + "CALOR_REGENERATE_CALOR0425_LEDGER=1.");
        Assert.All(committed.PerSubject, subject =>
        {
            Assert.NotNull(subject.BindFailureCauses);
            Assert.NotNull(subject.BindFailureModules);
        });
    }

    /// <summary>
    /// Schema 4's collection members forced a hand-written
    /// <c>SubjectVolume.Equals</c>, replacing the compiler's member-wise one. It
    /// is now a hand-maintained list of every member, so schema 5 adds a field,
    /// someone forgets a line there, and the ledger's central pin SILENTLY STOPS
    /// DETECTING MOVEMENT in that field — the exact failure the ledger exists to
    /// prevent, and invisible because the test still passes. This makes adding a
    /// member fail loudly until the comparison is updated with it.
    /// </summary>
    [Fact]
    public void SubjectVolumeEquality_CoversEveryMember()
    {
        var members = typeof(SubjectVolume).GetProperties().Length;
        Assert.True(members == 26,
            $"SubjectVolume has {members} members; the hand-written Equals covers 26. Add the new "
            + "member to Equals(SubjectVolume?) — omitting it makes "
            + "Calor0425CorpusLedgerMatchesRecomputation blind to that field — then update this "
            + "count.");
    }

    /// <summary>
    /// Gate 13 (roadmap-v0.17 §5). The bind-failure histogram must SUM to
    /// <c>ExcludedBindFailed</c> on every subject, and name one module per
    /// exclusion. A cause that does not add up is a red gate, not a rounding
    /// note: the histogram exists so §3.1 R2 can be scoped against a real
    /// cluster, and one that loses modules would scope it against a smaller
    /// cluster than the corpus holds.
    /// </summary>
    [Fact]
    public void Gate13_BindFailureCauses_SumToExcludedBindFailed()
    {
        var committed = CommittedLedger();
        AssertSchema4(committed);
        foreach (var subject in committed.PerSubject)
        {
            Assert.Equal(subject.ExcludedBindFailed, subject.BindFailureCauses.Values.Sum());
            Assert.Equal(subject.ExcludedBindFailed, subject.BindFailureModules.Count);
        }
    }

    /// <summary>
    /// v0.17 R1's deliverable to §3.1 R2: the largest binding cluster, named.
    /// <para>The RULE this feeds is frozen in roadmap-v0.17 §4.1 and was written
    /// BEFORE this measurement existed — R2 recovers at least half the largest
    /// cluster and never fewer than 10 modules. R1 supplies the cluster's SIZE
    /// and may not choose the fraction or the floor (review round 3, finding
    /// R3-b: deferring the whole effect size to R1 would have let whoever writes
    /// R1's PR pick the target with the breakdown already in front of them).
    /// Both constants are asserted here, so moving one is a diff on a pin rather
    /// than a quiet re-scoping.</para>
    /// </summary>
    [Fact]
    public void R1_NamesTheLargestBindingCluster_AndTheFrozenRuleSizesR2()
    {
        var committed = CommittedLedger();
        AssertSchema4(committed);
        var aggregate = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var subject in committed.PerSubject)
            foreach (var cause in subject.BindFailureCauses)
                aggregate[cause.Key] = aggregate.GetValueOrDefault(cause.Key) + cause.Value;

        // THE REGISTRATION IS HISTORY AND DOES NOT MOVE. R1 measured the cluster
        // at 40 over 60 stops, and §4.1's rule — frozen in review round 3 BEFORE
        // R1 ran — sized R2's target from that number. Re-deriving the target
        // from the CURRENT cluster after R2 shrank it is precisely the moving
        // goalpost finding R3-b forbade: the experiment would size its own
        // ambition to whatever it had already achieved.
        const int RegisteredLargestCluster = 40;      // R1's measurement
        const int RegisteredTotalBindStops = 60;      // R1's measurement
        const double RecoverAtLeastFraction = 0.5;    // §4.1, frozen
        const int NeverFewerThan = 10;                // §4.1, frozen
        var target = Math.Max(
            (int)Math.Ceiling(RegisteredLargestCluster * RecoverAtLeastFraction), NeverFewerThan);
        Assert.Equal(20, target);

        // PP-R1 may read UNDERPOWERED on ONE condition: the corpus could not
        // supply the effect under a rule fixed before the data was seen. At 40
        // it could, so that route is closed and the outcome is HIT or MISS.
        Assert.True(RegisteredLargestCluster >= NeverFewerThan);

        // WHERE THE CLUSTER STANDS NOW. R2 (overload assignability) moved it;
        // this asserts the direction, never the target.
        var largest = aggregate.MaxBy(entry => entry.Value);
        Assert.Equal("Calor0208", largest.Key);
        Assert.True(largest.Value <= RegisteredLargestCluster,
            $"the Calor0208 cluster is {largest.Value}, above R1's registered {RegisteredLargestCluster} "
            + "— a regression in the very cause R2 was scoped against.");
        Assert.True(aggregate.Values.Sum() <= RegisteredTotalBindStops,
            $"binding stops total {aggregate.Values.Sum()}, above R1's registered "
            + $"{RegisteredTotalBindStops}.");

        // R2's OUTCOME, as measured: 304 -> 319 enforced is +15 against a target
        // of 20, so PP-R1 leg 1 reads MISS. Recorded as an assertion so the
        // shortfall cannot quietly become a pass later.
        const int EnforcedAtR1 = 304;
        var enforcedNow = committed.PerSubject.Sum(s => s.ModulesEnforced);
        var recovered = enforcedNow - EnforcedAtR1;
        Assert.True(recovered >= 0, $"ModulesEnforced fell to {enforcedNow} from {EnforcedAtR1}.");
        Assert.True(recovered < target,
            $"ModulesEnforced recovered {recovered} modules, reaching the frozen target of {target}. "
            + "PP-R1 leg 1 is recorded as a MISS in roadmap-v0.17 §3.1 — update that outcome, this "
            + "assertion, and the release notes together.");
    }

    /// <summary>
    /// v0.17 R1 / review round 1 finding M1 — the denominator the IL-rows trigger
    /// was being read without.
    /// <para>This class's own comment records that §13.4's "unresolved receiver"
    /// never reaches Calor0425: the bare-target guard sends it out as Calor0411
    /// through the unknown-call chain. The trigger reads
    /// <c>UnknownSource + InvocationUndetermined</c> over the enforced set, so a
    /// zero there said nothing about the adjacent class until that class was
    /// counted over the SAME set.</para>
    /// <para><b>What this number is not.</b> Calor0411 is every unknown external
    /// call, not only the delegate-returning ones the IL-rows item is about, so
    /// it is an UPPER BOUND on that demand and not a measure of it. Splitting it
    /// is the next question, not an answered one — asserted here so the count is
    /// never read as "the IL-rows trigger really fires".</para>
    /// </summary>
    [Fact]
    public void R1_Calor0411_IsCountedOverTheEnforcedSet()
    {
        var committed = CommittedLedger();
        AssertSchema4(committed);
        var sites = committed.PerSubject.Sum(s => s.Calor0411Sites);
        var enforced = committed.PerSubject.Sum(s => s.ModulesEnforced);

        Assert.True(sites > 0, "Calor0411 is uncounted again — M1's whole point.");

        // PER SUBJECT, not on the aggregate. "Counted over the enforced set" is a
        // per-subject invariant, and an aggregate check would pass while MediatR
        // reported 40 Calor0411 modules over 31 enforced — the 304 total absorbing
        // it. Gate 13 above loops per subject for the same reason.
        foreach (var subject in committed.PerSubject)
            Assert.True(subject.Calor0411Modules <= subject.ModulesEnforced,
                $"{subject.Subject}: Calor0411 is counted over the ENFORCED set, so "
                + $"{subject.Calor0411Modules} may not exceed {subject.ModulesEnforced}.");

        // The contrast M1 asked for, as an assertion rather than a sentence: the
        // trigger's own fields read zero over exactly this set.
        var trigger = committed.PerSubject.Sum(s => s.UnknownSource + s.InvocationUndetermined);
        Assert.True(trigger == 0,
            $"UnknownSource + InvocationUndetermined is {trigger}, not 0. This is not a defect — "
            + "it is the IL-rows demand the roadmap's DEFERRED list is waiting for, and it fires "
            + "at > 10. Update this pin to the measured value, and re-read the IL-rows trigger "
            + "against it rather than against the Calor0411 upper bound below.");
        Assert.True(sites > trigger,
            "if this ever inverts, the trigger is measuring the larger class and M1 is closed.");
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
    ///
    /// <para><b>v0.16 K1 widened it, because the narrow version published a
    /// wrong cell.</b> Schema 2 matched only <c>§C{name}</c> / <c>§C{name.</c>
    /// and reported <c>RowlessNeverInvoked = 0</c> everywhere — vacuously, since
    /// it had no row-less destinations at all. Under the production rule the
    /// split becomes load-bearing (roadmap §6 registers "K1's never-invoked
    /// fraction" as the input to design-doc Q4), and the first non-zero value it
    /// produced was an artifact: <c>_errorMessageFactory</c> in
    /// <c>FluentValidation/Internal/RuleComponent.cs</c> is invoked twice, but
    /// through C# <c>?.Invoke(…)</c>, which the converter emits as the interop
    /// member-access form <c>(?. _errorMessageFactory "Invoke(context, value)")</c>
    /// — no <c>§C{…}</c> anywhere. Both interop forms are matched now.</para>
    ///
    /// <para><b>Still an under-approximation, and the ledger's Scope says so.</b>
    /// A delegate reached through a field chain, through an alias, or invoked
    /// under a name this probe cannot spell is invisible to it, so
    /// <c>RowlessNeverInvoked</c> is an UPPER bound on "never invoked" and
    /// <c>RowlessInvoked</c> a lower bound. Q4 must read it that way.</para>
    /// </summary>
    private static bool IsInvokedInModule(string source, string name) =>
        name.Length > 0
        && (source.Contains($"§C{{{name}}}", StringComparison.Ordinal)
            || source.Contains($"§C{{{name}.", StringComparison.Ordinal)
            // C# `f?.Invoke(…)` / `f.Invoke(…)` on a function-typed position
            // become interop member-access expressions, not §C calls.
            || source.Contains($"(?. {name} \"Invoke", StringComparison.Ordinal)
            || source.Contains($"(. {name} \"Invoke", StringComparison.Ordinal));

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
        int RawBagBindFailed,
        /// <summary>v0.17 R1 (schema 4) — <c>ExcludedBindFailed</c> broken out by
        /// the diagnostic the shipping compiler STOPS on, which is the first
        /// propagated error. These counts sum to <c>ExcludedBindFailed</c>
        /// (gate 13), and the largest entry across all subjects is the cluster
        /// roadmap-v0.17 §3.1 R2 is scoped against. Before this, the 60 modules
        /// were a count with no causes anywhere in the tree.</summary>
        SortedDictionary<string, int> BindFailureCauses,
        /// <summary>v0.17 R1 (schema 4). Each excluded module as
        /// "<c>Code path</c>", so the cluster R2 targets can be opened rather
        /// than trusted.</summary>
        List<string> BindFailureModules,
        /// <summary>v0.17 R1 (schema 4). Modules whose propagated bag holds MORE
        /// THAN ONE distinct code, so their attribution to a single cluster is a
        /// choice of the binder's report order. Published because §4.1 sizes R2
        /// off the largest cluster: a module counted in it that ALSO stops on
        /// another code cannot be recovered by fixing that cluster alone, and a
        /// large number here means the cluster is softer than its size suggests.
        /// </summary>
        int BindFailureMultiCause,
        /// <summary>v0.17 R1 (schema 4) — Calor0411 over the ENFORCED set. The
        /// unresolved-receiver class never reaches Calor0425 (see the class
        /// comment): the bare-target guard sends it out through the unknown-call
        /// chain instead. The IL-rows trigger reads
        /// <c>UnknownSource + InvocationUndetermined</c>, so without this it was
        /// being read against a partial denominator.</summary>
        int Calor0411Sites,
        int Calor0411Modules)
    {
        /// <summary>
        /// Schema 4 added two COLLECTION members, and a positional record's
        /// synthesized equality compares members with
        /// <see cref="EqualityComparer{T}.Default"/> — which for
        /// <see cref="List{T}"/> and <see cref="SortedDictionary{TKey,TValue}"/>
        /// is REFERENCE equality. Without this override the committed-vs-measured
        /// comparison in
        /// <c>Calor0425CorpusLedgerMatchesRecomputation</c> compares two distinct
        /// instances and is unequal on every run, turning the ledger's central
        /// pin into a permanent red that says "volume moved" when nothing did.
        /// Caught by that test on the first run of schema 4.
        /// </summary>
        public bool Equals(SubjectVolume? other) =>
            other is not null
            && (Subject, Diagnostics, ModulesWithDiagnostics, ModulesEnforced, ModulesNotMeasured)
               == (other.Subject, other.Diagnostics, other.ModulesWithDiagnostics,
                   other.ModulesEnforced, other.ModulesNotMeasured)
            && (RowlessDestination, UnknownSource, Assumed, RowlessInvoked, RowlessNeverInvoked)
               == (other.RowlessDestination, other.UnknownSource, other.Assumed,
                   other.RowlessInvoked, other.RowlessNeverInvoked)
            && (ExternalBase, InvocationRowless, InvocationUndetermined, InvocationAssumed,
                InvocationWitness)
               == (other.ExternalBase, other.InvocationRowless, other.InvocationUndetermined,
                   other.InvocationAssumed, other.InvocationWitness)
            && (ExcludedConversionFailed, ExcludedParseFailed, ExcludedBindFailed,
                ExcludedEffectPassFaulted)
               == (other.ExcludedConversionFailed, other.ExcludedParseFailed,
                   other.ExcludedBindFailed, other.ExcludedEffectPassFaulted)
            && (ModulesEnforcedRawBagRule, RawBagBindFailed, Calor0411Sites, Calor0411Modules,
                BindFailureMultiCause)
               == (other.ModulesEnforcedRawBagRule, other.RawBagBindFailed, other.Calor0411Sites,
                   other.Calor0411Modules, other.BindFailureMultiCause)
            && (BindFailureCauses ?? []).SequenceEqual(other.BindFailureCauses ?? [])
            && (BindFailureModules ?? []).SequenceEqual(other.BindFailureModules ?? []);

        public override int GetHashCode() =>
            HashCode.Combine(Subject, Diagnostics, ModulesEnforced, ExcludedBindFailed,
                Calor0411Sites, BindFailureCauses?.Count ?? 0, BindFailureModules?.Count ?? 0);
    }

    /// <summary>v0.16 K1 — one per-subject leg of gate 9's <c>ModulesEnforced</c>
    /// floor.</summary>
    private sealed record SubjectFloor(string Subject, int ModulesEnforcedMin);

    /// <summary>v0.16 K1 — one subject's row of N:S2.2's CLI outcome table,
    /// recorded beside the in-process numbers it must agree with.</summary>
    private sealed record CliCrossCheckSubject(
        string Subject,
        int Files,
        int ParseFailed,
        int BindStopped,
        int ReachEffectPass,
        int StopCalor0410,
        int StopCalor0422Or0423,
        int StopCalor1002,
        int CompileClean,
        /// <summary>
        /// v0.16 W3(a): modules the CLI stops at one of the DOCUMENTED CLI-ONLY
        /// passes (<c>Program.cs:760-808</c> — TypeChecker, PatternChecker,
        /// BindValidationPass, ReturnValidationPass) after binding and before the
        /// effect pass. They are inside <c>ReachEffectPass</c> — the in-process
        /// leg has no such pass and enforces them — but they produce no effect-pass
        /// outcome, so they are their own bucket rather than being smuggled into
        /// one of the four. Zero at K1's registration; three after W3(a), all of
        /// them newly-parsing FluentValidation modules stopping at Calor0209
        /// (IllegalYield, ReturnValidationPass): EmptyTester, NotEmptyTester and
        /// ITestValidationContinuation.
        /// </summary>
        int StoppedInCliOnlyPass,
        int Calor0425Modules,
        int Calor0425Sites);

    /// <summary>v0.16 K1 — the whole CLI cross-check, with the invocation it was
    /// measured under.</summary>
    private sealed record CliCrossCheck(
        string Invocation,
        string MeasuredAt,
        List<CliCrossCheckSubject> PerSubject);

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
        /// <summary>v0.16 K1 — the number schema 2's raw-bag guard produced (99).
        /// Registered so the discriminating pin's backstop can name the value the
        /// production denominator must NOT land on.</summary>
        int RawBagDenominatorAtRegistration,
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
        /// <summary>v0.16 K1 — §S2's second measurement (the pinned CLI pass),
        /// recorded so the two legs' agreement is auditable from the JSON and so
        /// a regeneration cannot silently drop it.</summary>
        CliCrossCheck CliCrossCheck,
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
