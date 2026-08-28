using System.Text.Json;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

// Flat test namespace: see BinderDispatchCompletenessTests.
namespace Calor.Compiler.Tests;

/// <summary>
/// v0.15 E1 slice 2a, review round 1 finding 2 — the Calor0270 volume ledger.
///
/// <para>The binder marks every receiver it cannot type with
/// <c>UnresolvedBoundType</c>, but only <i>reports</i> Calor0270 for the shapes an
/// author can act on. That split is a judgement call about editor noise, and a
/// judgement call with no instrument drifts. This is the instrument: how many
/// Calor0270 diagnostics the binder emits over the three pinned conversion
/// subjects, and how many converted modules carry at least one.</para>
///
/// <para>The ledger is an <b>exact-equality</b> pin in both directions, per subject
/// and in aggregate — the same discipline as the S5 resolution ledger
/// (<see cref="MetadataBinderCorpusMeasurementTests"/>). A <i>rise</i> means a new
/// unactionable shape started reporting and the editor got noisier; a <i>fall</i>
/// means a shape that authors could act on went silent. Both are decisions, and
/// both must be made deliberately in a PR that regenerates the ledger and says
/// what moved — never absorbed silently.</para>
///
/// <para>Regenerate: <c>CALOR_REGENERATE_CALOR0270_LEDGER=1 dotnet test
/// --filter Calor0270CorpusVolume</c>.</para>
///
/// <para>Both sides of the narrowing were measured with this same instrument, by
/// forcing <c>Binder.ShouldReportUnresolvedReceiver</c> to true and re-running:</para>
/// <list type="bullet">
/// <item><b>Reporting every unresolved receiver:</b> 875 diagnostics across 76 of
/// 305 bound modules (MediatR 49/10, Serilog 52/13, FluentValidation 774/53) —
/// dominated by member chains (<c>result.IsValid.X</c>) and converter-synthesized
/// temporaries (<c>_chainNNN</c>), neither of which an author can act on.</item>
/// <item><b>Reporting only the two actionable shapes</b> (the committed ledger):
/// 193 across 38 of 305 — a 78% reduction, and what remains is an inferred local
/// or a declared type string, both of which an explicit type annotation fixes.</item>
/// </list>
///
/// <para>Skipped when submodules are not initialized; the <c>compiler</c> shard
/// checks them out.</para>
///
/// <para><b>v0.16 K1 — the bind rule, named (roadmap §3.1 K1, §6 row 2).</b> This
/// ledger has <i>no</i> bind guard at all: it binds every module that PARSES
/// (305 of 364), counts the Calor0270 <i>Infos</i> the binder reports, and never
/// asks whether the binder's bag has errors. That is the right rule for what it
/// measures — Calor0270 is emitted BY the binder, so a module whose binding also
/// produced errors still produced these Infos — but it is a DIFFERENT rule from
/// the one the Calor0425 ledger uses (<c>"propagated"</c>: the shipping
/// compiler's filter through <c>BindingDiagnosticPolicy.PropagateCompilationErrors</c>
/// before the stop at <c>Program.cs:829-833</c>). The published "8 Calor0425
/// sites over 99 modules" was a ledger artifact precisely because nobody could
/// tell from the JSON which rule produced the denominator. So each ledger now
/// carries its own <c>BindRule</c> — <c>"parsed"</c> here, <c>"propagated"</c>
/// there — and the two are readable side by side.</para>
///
/// <para>K1 adds that field and <b>nothing else</b>: no number in
/// <c>calor0270-corpus-ledger.json</c> is regenerated, and the field was written
/// into the JSON by hand for exactly that reason.</para>
/// </summary>
public class Calor0270CorpusVolumeTests
{
    /// <summary>
    /// v0.16 K1 — this ledger's rule: bind every module that PARSES, with no
    /// bind guard. Contrast <c>Calor0425CorpusLedgerTests.BindRuleText</c>
    /// (<c>"propagated"</c>).
    /// </summary>
    internal const string BindRuleText = "parsed";

    private const string ScopeText =
        "Calor0270 (SignatureUnresolved) emitted by Binder.Bind over the three A-1.5.3 "
        + "conversion subjects at their pinned submodule commits, converted in-process with "
        + "Lossy/SelectActiveBranchLossy and genuinely empty default preprocessor symbols; "
        + "modules that fail conversion or whose converted output fails to parse are excluded "
        + "from the denominator and counted separately";

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Directory.Build.props")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }

    private static string LedgerPath() => Path.Combine(RepoRoot(),
        "bench", "phase0-agent-native", "calor0270-corpus-ledger.json");

    [SkippableFact]
    public void Calor0270Volume_OverConvertedCorpus_MatchesLedger()
    {
        var root = RepoRoot();
        var subjectDirs = new[] { "MediatR", "serilog", "FluentValidation" }
            .Select(subject => (Name: subject,
                Path: Path.Combine(root, "bench", "corpus", subject, "src")))
            .ToList();
        Skip.IfNot(subjectDirs.All(s => Directory.Exists(s.Path)),
            "corpus submodules not initialized");

        var perSubject = new List<SubjectVolume>();
        foreach (var subject in subjectDirs)
        {
            perSubject.Add(MeasureSubject(subject.Name, subject.Path));
            var last = perSubject[^1];
            Console.WriteLine(
                $"Calor0270-corpus {last.Subject}: {last.Diagnostics} diagnostics across "
                + $"{last.ModulesWithDiagnostics} of {last.ModulesBound} bound modules");
        }

        var measured = new Ledger(
            ScopeText,
            BindRuleText,
            perSubject.Sum(s => s.Diagnostics),
            perSubject.Sum(s => s.ModulesWithDiagnostics),
            perSubject.Sum(s => s.ModulesBound),
            perSubject);

        Console.WriteLine(
            $"Calor0270-corpus aggregate: {measured.AggregateDiagnostics} diagnostics across "
            + $"{measured.AggregateModulesWithDiagnostics} of {measured.AggregateModulesBound} bound modules");

        var ledgerPath = LedgerPath();
        var regenerate = string.Equals(
            Environment.GetEnvironmentVariable("CALOR_REGENERATE_CALOR0270_LEDGER"),
            "1", StringComparison.Ordinal);
        if (regenerate)
        {
            File.WriteAllText(ledgerPath, JsonSerializer.Serialize(
                measured, new JsonSerializerOptions { WriteIndented = true }) + "\n");
            Console.WriteLine($"Calor0270 corpus ledger regenerated: {ledgerPath}");
            return;
        }

        // A missing ledger is a failure, never a silent regeneration (R2-A).
        Assert.True(File.Exists(ledgerPath),
            $"Calor0270 corpus ledger missing at {ledgerPath} — run once with CALOR_REGENERATE_CALOR0270_LEDGER=1");
        var committed = JsonSerializer.Deserialize<Ledger>(File.ReadAllText(ledgerPath))!;

        // Anti-vacuity: a ledger that measured nothing would pass every equality
        // below. The corpus binds hundreds of modules; assert the denominator.
        Assert.True(measured.AggregateModulesBound > 250,
            $"Only {measured.AggregateModulesBound} modules bound — the corpus denominator "
            + "collapsed, so the equalities below would be vacuous.");

        Assert.Equal(committed.PerSubject.Count, measured.PerSubject.Count);
        foreach (var (expected, actual) in committed.PerSubject.Zip(measured.PerSubject))
        {
            Assert.Equal(expected.Subject, actual.Subject);
            Assert.True(expected == actual,
                $"Calor0270 volume moved for {actual.Subject}: committed "
                + $"{expected.Diagnostics} diagnostics / {expected.ModulesWithDiagnostics} modules "
                + $"(of {expected.ModulesBound}), measured {actual.Diagnostics} / "
                + $"{actual.ModulesWithDiagnostics} (of {actual.ModulesBound}). A RISE means an "
                + "unactionable receiver shape started reporting and the editor got noisier; a "
                + "FALL means a shape authors could act on went silent. Either way, regenerate "
                + "the ledger IN THIS PR with CALOR_REGENERATE_CALOR0270_LEDGER=1 and say what "
                + "moved — never absorb it silently.");
        }

        Assert.Equal(committed.AggregateDiagnostics, measured.AggregateDiagnostics);
        Assert.Equal(committed.AggregateModulesWithDiagnostics, measured.AggregateModulesWithDiagnostics);
        Assert.Equal(committed.AggregateModulesBound, measured.AggregateModulesBound);
        Assert.Equal(ScopeText, committed.Scope);

        // v0.16 K1 — the rule, in the file, beside the numbers it produced.
        Assert.Equal(BindRuleText, committed.BindRule);
    }

    /// <summary>
    /// The ledger's aggregate as it stood BEFORE v0.16 K1, restated so that
    /// "K1 changed the schema and nothing else" is observable rather than merely
    /// asserted in a PR body.
    ///
    /// <para><b>This baseline is expected to move, and the next author should
    /// know why before it does.</b> <c>AggregateModulesBound</c> is "modules that
    /// PARSE" = 364 − 59 parse failures. v0.16 W3(a) (#903 clusters 1–2,
    /// PR #1125) recovers 57 of those 59, so <b>this constant and the ledger it
    /// pins must be updated inside #1125's own PR</b>, alongside the Calor0425
    /// ledger's <c>ExcludedParseFailed</c> flip — the newly-parsing modules will
    /// also contribute Calor0270 Infos. That is a regeneration of the Calor0270
    /// ledger (<c>CALOR_REGENERATE_CALOR0270_LEDGER=1</c>) with the cause named,
    /// not an edit of these constants alone.</para>
    /// </summary>
    private const int PreK1AggregateDiagnostics = 193;
    private const int PreK1AggregateModulesWithDiagnostics = 38;
    private const int PreK1AggregateModulesBound = 305;

    /// <summary>
    /// v0.16 K1 (roadmap §3.1 K1, §6 row 2) — the schema field, and only the
    /// schema field. This ledger's numbers are NOT regenerated by K1, so the pin
    /// is: the bind rule is recorded, it is this ledger's own rule
    /// (<c>"parsed"</c>), it is not the Calor0425 ledger's rule, and the
    /// aggregate the rule produced is unchanged from the pre-K1 file. Runs
    /// without the corpus submodules — it reads the committed JSON. See
    /// <see cref="PreK1AggregateModulesBound"/> for when these constants are
    /// expected to move and which PR must move them.
    /// </summary>
    [Fact]
    public void Calor0270Ledger_NamesItsBindRule_AndIsNotRegeneratedByK1()
    {
        var committed = JsonSerializer.Deserialize<Ledger>(File.ReadAllText(LedgerPath()))!;

        Assert.Equal(BindRuleText, committed.BindRule);
        Assert.NotEqual(Calor0425CorpusLedgerTests.BindRuleText, committed.BindRule);

        Assert.Equal(PreK1AggregateDiagnostics, committed.AggregateDiagnostics);
        Assert.Equal(
            PreK1AggregateModulesWithDiagnostics, committed.AggregateModulesWithDiagnostics);
        Assert.Equal(PreK1AggregateModulesBound, committed.AggregateModulesBound);
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

        int diagnostics = 0, modulesWithDiagnostics = 0, modulesBound = 0, notMeasured = 0;
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
                        ModuleName = "Calor0270Leg",
                        GracefulFallback = true,
                        AutoGenerateIds = true
                    }).Convert(File.ReadAllText(file), Path.GetFileName(file));
            }
            catch
            {
                notMeasured++;
                continue;
            }

            if (string.IsNullOrEmpty(conversion.CalorSource))
            {
                notMeasured++;
                continue;
            }

            var parseDiagnostics = new DiagnosticBag();
            var module = new Parser(
                new Lexer(conversion.CalorSource.Replace("\r\n", "\n"), parseDiagnostics)
                    .TokenizeAllForParser(),
                parseDiagnostics).Parse();
            if (parseDiagnostics.HasErrors)
            {
                notMeasured++;
                continue;
            }

            var bindDiagnostics = new DiagnosticBag();
            new Binder(bindDiagnostics).Bind(module);
            modulesBound++;

            var count = bindDiagnostics.Count(d => d.Code == DiagnosticCode.SignatureUnresolved);
            diagnostics += count;
            if (count > 0)
                modulesWithDiagnostics++;
        }

        return new SubjectVolume(name, diagnostics, modulesWithDiagnostics, modulesBound, notMeasured);
    }

    private sealed record SubjectVolume(
        string Subject,
        int Diagnostics,
        int ModulesWithDiagnostics,
        int ModulesBound,
        int ModulesNotMeasured);

    private sealed record Ledger(
        string Scope,
        /// <summary>v0.16 K1 — <c>"parsed"</c>: every module that PARSES is
        /// bound and counted, with no bind guard. The Calor0425 ledger carries
        /// <c>"propagated"</c>, the shipping compiler's rule.</summary>
        string BindRule,
        int AggregateDiagnostics,
        int AggregateModulesWithDiagnostics,
        int AggregateModulesBound,
        List<SubjectVolume> PerSubject);
}
