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
/// </summary>
public class Calor0425CorpusLedgerTests
{
    private const int SchemaVersion = 2;

    private const string ScopeText =
        "Calor0425 (EffectRowUnknown) emitted by EffectEnforcementPass over the three A-1.5.3 "
        + "conversion subjects at their pinned submodule commits, converted in-process with "
        + "Lossy/SelectActiveBranchLossy and genuinely empty default preprocessor symbols, then "
        + "enforced with UnknownCallPolicy.Strict and no --permissive-effects; modules that fail "
        + "conversion or whose converted output fails to parse are excluded from the denominator "
        + "and counted separately. Causes are the ones the FIVE MONOMORPHIC SITES of design-doc "
        + "§6.2 can distinguish, plus (schema 2, v0.15 E4) the external-base arm of sites 4/5 and "
        + "the three verdicts an INVOCATION of a function-typed value can draw — row-less "
        + "declaration, undetermined source (a BCL-returned or row-less-returned value), and an "
        + "Assumed row; §13.4's unresolved-receiver cause is Calor0411's, not this ledger's";

    private static readonly string[] Subjects = ["MediatR", "serilog", "FluentValidation"];

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
                + $"/ bind {subject.ExcludedBindFailed})");
        }

        var measured = new Ledger(
            SchemaVersion,
            ScopeText,
            MeasuredCommit(root),
            perSubject.Sum(s => s.Diagnostics),
            perSubject.Sum(s => s.ModulesWithDiagnostics),
            perSubject.Sum(s => s.ModulesEnforced),
            perSubject.Sum(s => s.ModulesNotMeasured),
            perSubject);

        Console.WriteLine(
            $"Calor0425-corpus aggregate: {measured.AggregateDiagnostics} across "
            + $"{measured.AggregateModulesWithDiagnostics} of {measured.AggregateModulesEnforced}");

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

        // Anti-vacuity. A ledger that enforced nothing would satisfy every
        // equality below, including a per-subject zero.
        Assert.True(measured.AggregateModulesEnforced > 90,
            $"Only {measured.AggregateModulesEnforced} modules were enforced — the corpus "
            + "denominator collapsed, so the equalities below would be vacuous.");

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

            // The effect pass recurses without a depth bound, and a Lossy
            // conversion of a large corpus module nests far deeper than any
            // hand-written Calor: measured, `serilog/src/Serilog/Core/Logger.cs`
            // and `Core/Sinks/Batching/BatchingSink.cs` take the whole test host
            // down. That is PRE-EXISTING — it reproduces with v0.15 E3's row
            // checking disabled — and is tracked as issue #1104.
            //
            // THE CATCH BELOW CANNOT CATCH IT (review round 1, F8). A
            // StackOverflowException in .NET is a fail-fast: the process dies and
            // no catch block, on any thread, observes it. The 64 MB stack buys
            // headroom and nothing more, and measuring proved headroom alone is
            // not enough — the run still died with it in place. What actually
            // makes this test runnable is the BIND-FIRST GUARD below, which never
            // hands the pass a module the binder rejected. The thread and the
            // catch are belt to that brace: they contain ORDINARY exceptions,
            // which do occur, and they are documented here as NOT containing the
            // crash so nobody reads them as the remedy.
            var effectDiagnostics = new DiagnosticBag();
            // BIND FIRST, and skip a module the binder rejects. This is not a
            // convenience: `Program.Compile` returns as soon as binding has
            // errors, so a module that does not bind never reaches the effect
            // pass in the shipping compiler and its Calor0425s could never be
            // emitted. Counting them would inflate the ledger with diagnostics
            // no user can see.
            //
            // It is also what keeps this test runnable. Running the effect pass
            // directly over unbound Lossy conversions is fatal on several Serilog
            // modules (measured: Core/Logger.cs, Core/Sinks/Batching/BatchingSink.cs
            // take the test host down). That crash is PRE-EXISTING — it
            // reproduced with E3's row checking temporarily disabled —
            // and it is recorded in the E3a notes as owed, not fixed here.
            var bindDiagnostics = new DiagnosticBag();
            new Compiler.Binding.Binder(bindDiagnostics).Bind(module);
            if (bindDiagnostics.HasErrors)
            {
                notMeasured++;
                excludedBindFailed++;
                continue;
            }
            var faulted = false;
            var worker = new Thread(() =>
            {
                try
                {
                    new EffectEnforcementPass(effectDiagnostics).Enforce(module);
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
                excludedBindFailed++;
                continue;
            }

            enforced++;

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
            excludedConversionFailed, excludedParseFailed, excludedBindFailed);
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
        /// <summary>F7 — the exclusion-reason histogram. These three sum to
        /// <c>ModulesNotMeasured</c>.</summary>
        int ExcludedConversionFailed,
        int ExcludedParseFailed,
        int ExcludedBindFailed);

    private sealed record Ledger(
        int SchemaVersion,
        string Scope,
        string MeasuredCommit,
        int AggregateDiagnostics,
        int AggregateModulesWithDiagnostics,
        int AggregateModulesEnforced,
        /// <summary>Review round 1 (F7). Modules that never reached the effect
        /// pass — conversion threw, produced nothing, failed to parse, or (the
        /// overwhelming majority) failed to BIND. 73% of the corpus at this
        /// commit, so the headline zero is a zero over the remaining 27%.</summary>
        int AggregateModulesExcluded,
        List<SubjectVolume> PerSubject);
}
