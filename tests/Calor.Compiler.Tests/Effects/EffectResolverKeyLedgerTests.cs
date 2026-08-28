using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Parsing;
using Xunit;

// Flat test namespace: see BinderDispatchCompletenessTests.
namespace Calor.Compiler.Tests;

/// <summary>
/// v0.15 E1 slice 2c — the effect-resolver KEY ledger
/// (<c>bench/phase0-agent-native/effect-resolver-key-ledger.json</c>).
///
/// <para><b>What it measures, and why a ledger rather than an assertion.</b>
/// Exit pin (c) (roadmap §4.2 E1) is structural: it says no
/// <c>Resolve(string, string, …)</c> overload survives, and
/// <c>ArchitectureTests.EffectResolver_ExposesNoStringTypeNameResolveOverload</c>
/// enforces exactly that. But an API can be keyed on symbol identity while
/// every caller still funnels text through one factory — the deletion would
/// then be cosmetic. This ledger is the instrument that makes the difference
/// observable: for each subject it records how many keys the compiler builds
/// from a BOUND receiver's <c>BoundType</c> versus from a string fallback, and
/// pins both numbers exactly.</para>
///
/// <para><b>It freezes today's split, and takes no position on it.</b> The
/// numbers here are not a target. E1 slice 2c re-keys the resolver; it does not
/// widen what the binder can type, and the resolution ceiling (design doc §2.2:
/// 431 of 1248 BCL call sites unresolved) is untouched. The ledger exists so
/// that E2's work has a baseline to move, and so that a regression — a call
/// site quietly reverting from a bound key to a string key — is a red test
/// rather than an invisible drift.</para>
///
/// <para><b>Denominator.</b> Every committed <c>.calr</c> under the repository
/// root, with the same exclusions <c>HigherOrderDemandLedgerTests</c>'s D-A leg
/// uses (<c>bin/</c>, <c>obj/</c>, <c>.git/</c>, <c>.claude/</c>,
/// <c>node_modules/</c>, the <c>bench/corpus/</c> submodules, and
/// <c>docs/design/spikes/</c> artifacts), partitioned into SUBJECTS by
/// top-level directory. Each file is lexed, parsed, and run through
/// <see cref="EffectEnforcementPass"/> under
/// <see cref="UnknownCallPolicy.Strict"/> with a hermetic
/// <see cref="EffectResolver"/> — built-in manifests only, no project, solution
/// or user manifests.</para>
///
/// <para><b>Files that contribute no keys are pinned BY NAME</b> (review round
/// 1, MAJOR 5). A count alone lets one file start failing to parse while
/// another starts parsing and the total stays put; the name list makes that
/// swap a red test. It is also the honest form of the claim: the ledger says
/// which files it could not measure, not merely how many.</para>
///
/// <para><b>Anti-tautology.</b> Exact per-subject and aggregate equality
/// against the committed ledger, recomputed live — the
/// <c>MetadataBinderCorpusMeasurementTests</c> pattern — plus a
/// <c>measuredCommit</c> stamped from <c>HEAD</c> and asserted to be a 40-hex
/// SHA, and a pre-registered <c>floor</c> so a collapse to near-zero keys
/// cannot pass as "the numbers moved". A missing ledger file is a FAILURE, not
/// a silent regeneration (the #1095 R2-A pattern): regeneration happens only
/// under <c>CALOR_REGENERATE_EFFECT_RESOLVER_KEY_LEDGER=1</c>, so an instrument
/// deleted in a PR cannot quietly rewrite itself on the next run.</para>
///
/// <para><b>Skips</b> only when the repository holds no committed <c>.calr</c>
/// at all (a partial checkout). It needs no submodules: the subjects are
/// in-repo <c>.calr</c>, not the C# conversion corpora. It runs in the
/// <c>compiler</c> shard.</para>
/// </summary>
public class EffectResolverKeyLedgerTests
{
    private const string RegenerateEnvVar = "CALOR_REGENERATE_EFFECT_RESOLVER_KEY_LEDGER";

    /// <summary>
    /// Pre-registered with the ledger, and deliberately far below the measured
    /// total (953 keys at registration). It is a collapse detector, not a bar:
    /// if a refactor silently stops asking the resolver anything, every exact
    /// equality above would still be satisfiable by regenerating, and this is
    /// what makes that regeneration obviously wrong.
    /// </summary>
    private const int Floor = 200;

    private const string FloorRule =
        "Pre-registered with the ledger (v0.15 E1 slice 2c): keysFromBound + keysFromString "
        + "across all subjects must be at least 200. The floor detects a collapsed denominator "
        + "— a refactor that stops asking the resolver at all — which exact-equality alone "
        + "cannot catch, because a regenerated ledger of zeroes is self-consistent. It is not a "
        + "quality bar and is not re-tuned when the measurement moves.";

    private const string ScopeText =
        "Every committed .calr under the repository root (bin/, obj/, .git/, .claude/, "
        + "node_modules/, bench/corpus/ submodules and docs/design/spikes/ artifacts excluded; "
        + "nothing else filtered), partitioned into subjects by top-level directory. Each file is "
        + "lexed with TokenizeAllForParser and parsed, then run through EffectEnforcementPass "
        + "with UnknownCallPolicy.Strict and a hermetic EffectResolver (built-in manifests only; "
        + "ProjectDirectory and SolutionDirectory null). keysFromBound counts EffectResolverKey "
        + "instances built by EffectResolverKey.FromBoundReceiver — the receiver's type came from "
        + "the bound tree; keysFromString counts EffectResolverKey.FromStrings — the caller held "
        + "only text. Both are counted per CALL SITE, before the resolver's cache, so the ledger "
        + "measures how the compiler ASKS rather than how often an answer had to be computed. A "
        + "key's parameter component is the inferred types of the ARGUMENTS, not the callee's "
        + "resolved signature (design doc §8.4), so this ledger measures declaring-type "
        + "provenance only. Files that fail to lex/parse, or whose pass throws, contribute no "
        + "keys and are listed by repo-relative path in notMeasured.";

    private static readonly string RepoRoot = CliTestHarness.FindRepoRoot();

    [SkippableFact]
    public void KeyOrigins_OverCommittedCalorCorpus_MatchLedgerExactly()
    {
        Skip.If(
            EnumerateCalorFiles(RepoRoot).Count == 0,
            "no committed .calr found under the repository root — partial checkout");

        var perSubject = MeasureSubjects(RepoRoot);
        var aggregateBound = perSubject.Sum(s => s.KeysFromBound);
        var aggregateString = perSubject.Sum(s => s.KeysFromString);

        foreach (var subject in perSubject)
        {
            Console.WriteLine(
                $"key-ledger {subject.Subject}: bound {subject.KeysFromBound} / string "
                + $"{subject.KeysFromString} over {subject.FilesMeasured} files "
                + $"({subject.NotMeasured.Count} not measured)");
        }

        Console.WriteLine(
            $"key-ledger aggregate: bound {aggregateBound} / string {aggregateString}");

        // The floor is checked against the LIVE measurement, before any
        // comparison, so a collapsed denominator fails even in a regeneration
        // run — which is the only run that could otherwise enshrine it.
        Assert.True(
            aggregateBound + aggregateString >= Floor,
            $"key ledger measured {aggregateBound + aggregateString} keys, below the "
            + $"pre-registered floor of {Floor}. {FloorRule}");

        var ledgerPath = LedgerPath();

        if (string.Equals(
                Environment.GetEnvironmentVariable(RegenerateEnvVar), "1", StringComparison.Ordinal))
        {
            var ledger = new KeyLedger(
                SchemaVersion: 1,
                RegisteredAt: "2026-08-26",
                MeasuredCommit: HeadSha(),
                Floor: Floor,
                FloorRule: FloorRule,
                Scope: ScopeText,
                AggregateKeysFromBound: aggregateBound,
                AggregateKeysFromString: aggregateString,
                PerSubject: perSubject);
            File.WriteAllText(
                ledgerPath,
                JsonSerializer.Serialize(
                    ledger, new JsonSerializerOptions { WriteIndented = true }) + "\n");
            Console.WriteLine($"key ledger regenerated: {ledgerPath}");
            return;
        }

        // A missing ledger is a DELETED INSTRUMENT, not a first run. Writing one
        // here would let a PR remove the freeze and have it silently reappear
        // with whatever the PR's own numbers happen to be.
        Assert.True(
            File.Exists(ledgerPath),
            $"key ledger is missing at {ledgerPath}. It is committed; regenerate deliberately "
            + $"with {RegenerateEnvVar}=1 and disclose the delta.");

        using var stream = File.OpenRead(ledgerPath);
        var committed = JsonSerializer.Deserialize<JsonElement>(stream);

        Assert.Equal(committed.GetProperty("aggregateKeysFromBound").GetInt32(), aggregateBound);
        Assert.Equal(committed.GetProperty("aggregateKeysFromString").GetInt32(), aggregateString);

        var ledgerSubjects = committed.GetProperty("perSubject");
        Assert.Equal(ledgerSubjects.GetArrayLength(), perSubject.Count);
        for (var i = 0; i < perSubject.Count; i++)
        {
            var expected = ledgerSubjects[i];
            var live = perSubject[i];
            Assert.Equal(expected.GetProperty("subject").GetString(), live.Subject);
            Assert.Equal(expected.GetProperty("keysFromBound").GetInt32(), live.KeysFromBound);
            Assert.Equal(expected.GetProperty("keysFromString").GetInt32(), live.KeysFromString);
            Assert.Equal(expected.GetProperty("filesMeasured").GetInt32(), live.FilesMeasured);

            // BY NAME, not by count: a file that starts failing to parse while
            // another starts parsing keeps the count still and must not pass.
            Assert.Equal(
                expected.GetProperty("notMeasured").EnumerateArray()
                    .Select(e => e.GetString() ?? string.Empty).ToList(),
                live.NotMeasured);
        }
    }

    /// <summary>
    /// The ledger's own shape: it must not be able to pass by measuring nothing,
    /// its scope and floor must be the ones this test enforces, and its
    /// <c>measuredCommit</c> must be a real SHA rather than a placeholder.
    /// <c>registeredAt</c> is read FROM the ledger rather than compared to a
    /// literal here — a date the test also hard-codes proves nothing about the
    /// committed file.
    /// </summary>
    [Fact]
    public void Ledger_DeclaresItsScopeFloorAndMeasuredCommit()
    {
        var ledgerPath = LedgerPath();

        // Deliberately NOT a skip: the ledger is committed, so a missing file is
        // a deleted instrument, not an environment gap.
        Assert.True(File.Exists(ledgerPath), $"key ledger is missing at {ledgerPath}");

        using var stream = File.OpenRead(ledgerPath);
        var committed = JsonSerializer.Deserialize<JsonElement>(stream);

        var measured = committed.GetProperty("perSubject")
            .EnumerateArray()
            .Sum(s => s.GetProperty("filesMeasured").GetInt32());
        Assert.True(measured > 0, "key ledger measured zero files — the denominator collapsed");

        var totalKeys = committed.GetProperty("aggregateKeysFromBound").GetInt32()
            + committed.GetProperty("aggregateKeysFromString").GetInt32();
        Assert.True(
            totalKeys >= Floor,
            $"committed key ledger records {totalKeys} keys, below its own floor of {Floor}");

        Assert.Equal(ScopeText, committed.GetProperty("scope").GetString());
        Assert.Equal(Floor, committed.GetProperty("floor").GetInt32());
        Assert.Equal(FloorRule, committed.GetProperty("floorRule").GetString());

        var measuredCommit = committed.GetProperty("measuredCommit").GetString();
        Assert.True(
            measuredCommit != null && Regex.IsMatch(measuredCommit, "^[0-9a-f]{40}$"),
            $"measuredCommit must be a 40-hex commit SHA, was '{measuredCommit}'");

        // Read, not asserted against a literal: the point is that the committed
        // file carries a registration date at all.
        var registeredAt = committed.GetProperty("registeredAt").GetString();
        Assert.True(
            registeredAt != null && Regex.IsMatch(registeredAt, @"^\d{4}-\d{2}-\d{2}$"),
            $"registeredAt must be an ISO date, was '{registeredAt}'");
    }

    private static string LedgerPath() => Path.Combine(
        RepoRoot, "bench", "phase0-agent-native", "effect-resolver-key-ledger.json");

    private static List<KeySubject> MeasureSubjects(string root)
    {
        var bySubject = new SortedDictionary<string, SubjectTotals>(StringComparer.Ordinal);

        foreach (var file in EnumerateCalorFiles(root))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var subject = relative.Contains('/', StringComparison.Ordinal)
                ? relative[..relative.IndexOf('/', StringComparison.Ordinal)]
                : "(root)";

            if (!bySubject.TryGetValue(subject, out var totals))
            {
                totals = new SubjectTotals();
                bySubject[subject] = totals;
            }

            // One resolver per FILE keeps the measurement independent of file
            // order: the resolver's cache would otherwise make the count depend
            // on which file warmed which entry. Counts are per call site, so
            // caching never changes them — but the guarantee is cheap and the
            // ledger's determinism is the whole point.
            var resolver = new EffectResolver();
            resolver.Initialize();

            try
            {
                var diagnostics = new DiagnosticBag();
                var source = File.ReadAllText(file).Replace("\r\n", "\n");
                // TokenizeAllForParser, not Tokenize: the parser-facing token
                // stream is what Program.Compile feeds the parser, and the two
                // are not interchangeable — measuring through the wrong one
                // silently drops most of the corpus into notMeasured.
                var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
                var module = new Parser(tokens, diagnostics).Parse();
                if (diagnostics.HasErrors)
                {
                    totals.NotMeasured.Add(relative);
                    continue;
                }

                var effectDiagnostics = new DiagnosticBag();
                new EffectEnforcementPass(effectDiagnostics, UnknownCallPolicy.Strict, resolver)
                    .Enforce(module);
            }
            catch (Exception)
            {
                // Named, never dropped — see notMeasured.
                totals.NotMeasured.Add(relative);
                continue;
            }

            var origins = resolver.KeyOrigins;
            totals.Bound += (int)origins.FromBoundReceiver;
            totals.String += (int)origins.FromStringFallback;
            totals.Measured++;
        }

        return bySubject
            .Select(entry => new KeySubject(
                entry.Key,
                entry.Value.Bound,
                entry.Value.String,
                entry.Value.Measured,
                entry.Value.NotMeasured.OrderBy(f => f, StringComparer.Ordinal).ToList()))
            .ToList();
    }


    /// <summary>
    /// The PP-W-rows seeded mutants (roadmap v0.16 §4.1, S3 (c)):
    /// <c>bench/phase0-agent-native/pairs/W-00x-.../seeded/*.calr</c>. Measurement
    /// fixtures, excluded from the committed-corpus census like the spike artifacts.
    /// The per-arm starters beside them are NOT matched and stay counted.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex PpwSeededFixture =
        new(@"^bench/phase0-agent-native/pairs/W-\d{3}-[^/]+/seeded/",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static List<string> EnumerateCalorFiles(string root)
    {
        // Filter on REPO-RELATIVE paths, for the reason HigherOrderDemandLedgerTests
        // records: the checkout may itself live under a directory named like an
        // excluded segment (a worktree under `.claude/worktrees/`), and an
        // absolute-path filter would then match every file. Every dot-directory is
        // skipped, for the reason recorded there too: this is a filesystem walk, and
        // the harness's gitignored `epochs/**/.prev-src/` / `.envelope-src/` copies
        // would otherwise enter the denominator (1006 vs 926 on a clean tree, PR #1110;
        // the clean-tree count is 927 since #1104's crash-repro fixture).
        // No committed .calr lives under a dot-directory, so the counts are unchanged.
        return Directory.EnumerateFiles(root, "*.calr", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(rel =>
            {
                var segments = rel.Split('/');
                var directories = segments.Take(segments.Length - 1).ToList();
                return !directories.Any(d => d is "bin" or "obj" or "node_modules" || d.StartsWith('.'))
                    && !rel.StartsWith("bench/corpus/", StringComparison.Ordinal)
                    && !rel.StartsWith("docs/design/spikes/", StringComparison.Ordinal)
                    // Harness scratch that is not product corpus, excluded like the spike
                    // artifacts above: templates/ = the arm csproj template and the
                    // permissive canary (v0.16 W1), and the PP-W-rows SEEDED mutants
                    // (pairs/W-00x-*/seeded/, S3 (c)). The per-arm starters are ordinary
                    // programs and stay counted — §4.1 route (a) rests on them.
                    && !rel.StartsWith("bench/phase0-agent-native/templates/", StringComparison.Ordinal)
                    && !PpwSeededFixture.IsMatch(rel);
            })
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => Path.Combine(root, f))
            .ToList();
    }

    private static string HeadSha()
    {
        var psi = new ProcessStartInfo("git", "rev-parse HEAD")
        {
            WorkingDirectory = RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        Assert.True(
            process.ExitCode == 0 && Regex.IsMatch(output, "^[0-9a-f]{40}$"),
            $"could not resolve HEAD via `git rev-parse HEAD`: '{output}'");
        return output;
    }

    private sealed class SubjectTotals
    {
        public int Bound { get; set; }
        public int String { get; set; }
        public int Measured { get; set; }
        public List<string> NotMeasured { get; } = [];
    }

    private sealed record KeyLedger(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("registeredAt")] string RegisteredAt,
        [property: JsonPropertyName("measuredCommit")] string MeasuredCommit,
        [property: JsonPropertyName("floor")] int Floor,
        [property: JsonPropertyName("floorRule")] string FloorRule,
        [property: JsonPropertyName("scope")] string Scope,
        [property: JsonPropertyName("aggregateKeysFromBound")] int AggregateKeysFromBound,
        [property: JsonPropertyName("aggregateKeysFromString")] int AggregateKeysFromString,
        [property: JsonPropertyName("perSubject")] IReadOnlyList<KeySubject> PerSubject);

    private sealed record KeySubject(
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("keysFromBound")] int KeysFromBound,
        [property: JsonPropertyName("keysFromString")] int KeysFromString,
        [property: JsonPropertyName("filesMeasured")] int FilesMeasured,
        [property: JsonPropertyName("notMeasured")] IReadOnlyList<string> NotMeasured);
}
