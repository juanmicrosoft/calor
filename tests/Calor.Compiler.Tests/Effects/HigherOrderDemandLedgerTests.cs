using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Effects.Manifests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

// Flat test namespace: see BinderDispatchCompletenessTests.
namespace Calor.Compiler.Tests;

/// <summary>
/// v0.15 §4.1 — the higher-order demand ledger
/// (<c>bench/phase0-agent-native/higher-order-demand-ledger.json</c>), the
/// pre-registered denominator for §4.4 gate 2 ("higher-order expressiveness").
///
/// <para>Two denominators, both frozen in the ledger with the commit SHA the
/// measurement was taken at, so that the design doc opens with the two numbers
/// and cannot re-derive them:</para>
///
/// <list type="bullet">
/// <item><b>D-A (Calor-native):</b> every committed <c>.calr</c> in the repo is
/// compiled in-process under the DEFAULT effect policy (<c>EnforceEffects</c>
/// on, <c>UnknownCallPolicy.Strict</c> — never <c>--permissive-effects</c>) and
/// the ledger records (a) Calor0418 <c>DelegateInvocation</c> firings and (b)
/// Calor0419 <c>AssumedEffects</c> firings whose message names a
/// "function-typed value" — the argument-passing assumption recorded by
/// <c>EffectEnforcementPass.InferFromCallArguments</c>. A corpus written in a
/// language that rejects the idiom under-counts it — which is exactly why D-B
/// exists.</item>
/// <item><b>D-B (C#-shaped backstop):</b> a Roslyn syntax-level count over the
/// three A-1.5.3 conversion subjects (MediatR, Serilog, FluentValidation at
/// their pinned submodule commits) of lambdas, anonymous methods, delegate
/// declarations, delegate-typed declarations, and invocations of those
/// declared names. Independent of Calor's rejection.</item>
/// </list>
///
/// <para><b>Anti-tautology pin</b> (the <c>MetadataBinderCorpusMeasurementTests</c>
/// / <c>BinderIncompleteRatchetTests</c> pattern): each leg re-executes the
/// measurement live and asserts EXACT per-file / per-subject and aggregate
/// equality against the committed ledger. A number cannot be hand-edited into
/// the ledger without the measurement moving with it, and a measurement
/// cannot move without the ledger being regenerated in the same PR.
/// Regenerate: <c>CALOR_REGENERATE_HIGHER_ORDER_DEMAND_LEDGER=1 dotnet test
/// --filter HigherOrderDemandLedger</c> with submodules initialized — each leg
/// stamps its own <c>measuredCommit</c>, and the shape test requires both legs
/// (and the top-level SHA) to agree, so a one-legged regeneration fails loud.</para>
///
/// <para>D-A and the shape test run wherever <c>Calor.Compiler.Tests</c> runs;
/// D-B skips only where the corpus submodules are absent. In CI the project runs
/// in the <c>compiler</c> shard of <c>remaining-tests</c> (the manifest-enforcing
/// run) and in the <c>quality-ratchets</c> coverage sweep — both check out the
/// submodules, and the ratchet step in <c>test.yml</c> greps for the skip message
/// so a silent D-B skip fails the job.</para>
/// </summary>
public class HigherOrderDemandLedgerTests
{
    private const string RegenerateEnvVar = "CALOR_REGENERATE_HIGHER_ORDER_DEMAND_LEDGER";
    private const string RegisteredAt = "2026-08-24";
    private const int Floor = 25;
    private const string FloorRule =
        "Pre-registered (roadmap §4.1): if dA.total + dB.aggregate.total is below 25 sites, "
        + "§4.4 gate 2 adjudicates NOT-ADJUDICATED, never HIT. The floor is frozen with the "
        + "ledger's registration PR and is not re-tuned after the design doc opens.";

    private const string ScopeText =
        "D-A: every .calr under the repository root (bin/, obj/, .git/, .claude/, node_modules/ "
        + "and bench/corpus/ submodules excluded; nothing else filtered), compiled one file at a "
        + "time via Program.Compile with EnforceEffects=true, UnknownCallPolicy.Strict, "
        + "StrictEffects=false, EnableTypeChecking=true (the CLI default), "
        + "UnsafeTranspileOnly=false, DeferGeneratedOutputValidation=true (per-file Roslyn "
        + "validation of the emitted C# is skipped; it runs after the effect pass and cannot "
        + "affect the counts), ProjectDirectory=null (the CLI sets it to the file's directory; "
        + "here no project-local manifest is consulted), and a shared hermetic EffectResolver "
        + "loading built-in manifests only; no project/solution/user-level manifests. Files that "
        + "fail before the effect pass (lexer/parser/type-check/binder errors; witnessed by the "
        + "absence of Program.Compile's verbose 'Effect enforcement completed' status line) are "
        + "listed by name with their first error code in notReachingEffectPass, never dropped. "
        + "Calor0419 is counted per DIAGNOSTIC (one per function, first three reasons shown), so "
        + "a function with more than three assumptions whose function-typed reason is truncated "
        + "out of the message is under-counted — a known floor, not a ceiling. "
        + "D-B: Roslyn syntax-only (CSharpSyntaxTree.ParseText, LanguageVersion.Preview, no "
        + "preprocessor symbols defined, so inactive #if branches are not scanned) over "
        + "bench/corpus/{MediatR,serilog,FluentValidation}/src/**/*.cs (bin/, obj/ excluded). "
        + "delegate declarations are collected per SUBJECT (all files) before classifying, so a "
        + "delegate declared in one file counts where it is used in another.";

    private const string NotReachingEffectPassNote =
        "Known state at registration, not a filter: bench/mcp/tasks/*/expected.calr and "
        + "input.calr are MCP benchmark fixtures that are deliberately broken or written against "
        + "older syntax (30 of 33 die on Calor0830 legacy closing tags, two on Calor0006, one on "
        + "Calor0403), benchmarks/* entries are the #901 stale subjects, and "
        + "tests/TestData/LintScenarios/10_error_cases/* are error fixtures.";

    /// <summary>
    /// The D-B classification, pinned as data: identifiers a declared type syntax
    /// must resolve to (after unwrapping <c>?</c> and a namespace qualifier) to count
    /// as delegate-typed. Same-subject <c>delegate</c> declarations are added per subject.
    /// </summary>
    private static readonly string[] BclDelegateTypeNames =
        ["Func", "Action", "Predicate", "Comparison", "Converter", "EventHandler"];

    private static readonly IReadOnlyDictionary<string, string> Classes =
        new Dictionary<string, string>
        {
            ["dA.calor0418"] =
                "Calor0418 DelegateInvocation diagnostics (any severity) — invocation of a "
                + "parameter/§B/field value or of a returned delegate value under enforcement.",
            ["dA.calor0419FunctionTyped"] =
                "Calor0419 AssumedEffects diagnostics whose message contains 'function-typed "
                + "value' — a function-typed local/parameter passed as an argument to a dotted "
                + "(external) call target (EffectEnforcementPass.InferFromCallArguments).",
            ["dB.lambdas"] =
                "SimpleLambdaExpressionSyntax + ParenthesizedLambdaExpressionSyntax.",
            ["dB.anonymousMethods"] = "AnonymousMethodExpressionSyntax (delegate (...) { ... }).",
            ["dB.delegateDeclarations"] = "DelegateDeclarationSyntax (delegate R Name(...);).",
            ["dB.delegateTypedDeclarations"] =
                "Parameters, fields, event fields, properties and locals whose declared type "
                + "syntax (after unwrapping '?' and a namespace qualifier) is Func<…>, Action, "
                + "Action<…>, Predicate<…>, Comparison<…>, Converter<…>, EventHandler, "
                + "EventHandler<…>, or the name of a delegate declared anywhere in the same "
                + "subject. 'var' locals never count.",
            ["dB.delegateInvocations"] =
                "InvocationExpressionSyntax whose target is a bare identifier, or "
                + "identifier.Invoke / identifier?.Invoke, where the identifier is one of the "
                + "delegate-typed declaration names collected in the same file.",
        };

    private static readonly (string Name, string SubmodulePath)[] Subjects =
    [
        ("MediatR", "bench/corpus/MediatR"),
        ("Serilog", "bench/corpus/serilog"),
        ("FluentValidation", "bench/corpus/FluentValidation"),
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string RepoRoot() => CliTestHarness.FindRepoRoot();

    private static string LedgerPath() => Path.Combine(RepoRoot(),
        "bench", "phase0-agent-native", "higher-order-demand-ledger.json");

    private static bool Regenerate() =>
        Environment.GetEnvironmentVariable(RegenerateEnvVar) == "1";

    // ---------------------------------------------------------------- D-A

    [Fact]
    public void DA_CalorNative_MatchesLedgerExactly()
    {
        var measured = MeasureDA();
        Assert.True(measured.FileCount > 0, "D-A found no .calr files — corpus discovery broke.");

        if (Regenerate())
        {
            WriteLedger(existing => existing with { DA = measured with { MeasuredCommit = HeadSha() } });
            return;
        }

        var ledger = ReadLedger();
        Assert.NotNull(ledger.DA);
        var recorded = ledger.DA!;

        Assert.True(recorded.FileCount == measured.FileCount,
            $"D-A corpus size moved: {measured.FileCount} .calr files vs ledger {recorded.FileCount}. " +
            $"Corpus additions/removals regenerate the ledger IN THIS PR ({RegenerateEnvVar}=1) " +
            "with the change named — never silently. This is a filesystem walk: if the count is " +
            "higher than `git ls-files '*.calr'` (minus docs/design/spikes/), untracked or " +
            "gitignored .calr are being counted — harness scratch, or epoch run-internals " +
            "outside a dot-directory — and the tree must be cleaned before regenerating.");
        // Files that never reach the pass are pinned BY NAME and by first error code: a
        // parser/binder regression that silently removes files from the effective
        // denominator (or changes why they fail) fails here, naming them.
        var recordedFailing = recorded.NotReachingEffectPass.Select(e => e.File).ToList();
        var measuredFailing = measured.NotReachingEffectPass.Select(e => e.File).ToList();
        Assert.True(recordedFailing.SequenceEqual(measuredFailing),
            "D-A files failing before the effect pass moved — a parser/binder change shrank or " +
            "grew the effective denominator; regenerate with the change named.\n  newly failing: " +
            string.Join(", ", measuredFailing.Except(recordedFailing)) +
            "\n  newly reaching: " + string.Join(", ", recordedFailing.Except(measuredFailing)));
        Assert.Equal(recorded.NotReachingEffectPass, measured.NotReachingEffectPass);
        Assert.Equal(recorded.FilesNotReachingEffectPass, measured.FilesNotReachingEffectPass);
        Assert.Equal(recorded.CompileExceptions, measured.CompileExceptions);
        Assert.Equal(recorded.PerFile, measured.PerFile);
        Assert.True(recorded.Calor0418 == measured.Calor0418
                && recorded.Calor0419FunctionTyped == measured.Calor0419FunctionTyped
                && recorded.Total == measured.Total,
            $"D-A totals moved: 0418 {measured.Calor0418} / 0419(function-typed) " +
            $"{measured.Calor0419FunctionTyped} vs ledger {recorded.Calor0418} / " +
            $"{recorded.Calor0419FunctionTyped}. The ledger is the frozen denominator — " +
            "regenerate in this PR and name the cause (§4.4 gate 2 discriminating pin).");
    }

    private static DALedger MeasureDA()
    {
        var root = RepoRoot();
        var files = EnumerateCalorFiles(root);

        // Hermetic resolver: built-in manifests only. ManifestLoader.LoadAll would otherwise
        // consult ~/.calor/manifests/ and make the measurement depend on the machine.
        // EffectResolver.Initialize is idempotent, so the pass's own Initialize call is a no-op.
        var resolver = new EffectResolver(new ManifestLoader(loadUserLevelManifests: false));
        resolver.Initialize(projectDirectory: null, solutionDirectory: null);
        using var context = new CompilationContext { SharedEffectResolver = resolver };

        var perFile = new List<DAFileEntry>();
        var exceptions = new List<string>();
        var notReachingEffectPass = new List<DANotReachingEntry>();
        foreach (var file in files)
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            CompilationResult result;
            var status = new StringWriter();
            try
            {
                var options = new CompilationOptions
                {
                    EnableTypeChecking = true,
                    UnsafeTranspileOnly = false,
                    DeferGeneratedOutputValidation = true,
                    ProjectDirectory = null,
                    Context = context,
                    Verbose = true,
                    StatusWriter = status,
                };
                Assert.True(options.EnforceEffects, "default CompilationOptions must enforce effects");
                Assert.Equal(UnknownCallPolicy.Strict, options.UnknownCallPolicy);
                Assert.False(options.StrictEffects);
                result = Program.Compile(File.ReadAllText(file).Replace("\r\n", "\n"), file, options);
            }
            catch (Exception ex)
            {
                exceptions.Add($"{rel}: {ex.GetType().Name}");
                continue;
            }

            var diagnostics = result.Diagnostics.ToList();
            // The effect pass only runs once lexing, parsing, type checking, pattern/bind/
            // return validation and semantic binding are clean. Program.Compile's verbose
            // status line is the exact witness that the pass ran (diagnostic code bands
            // overlap across phases, so they are not). A file that dies earlier contributes
            // zero sites and is COUNTED as such rather than dropped from the denominator.
            if (!status.ToString().Contains(Program.EffectEnforcementCompletedStatus, StringComparison.Ordinal))
            {
                var firstError = diagnostics.FirstOrDefault(d => d.IsError)?.Code ?? "none";
                notReachingEffectPass.Add(new DANotReachingEntry(rel, firstError));
                continue;
            }

            var c0418 = diagnostics.Count(d => d.Code == DiagnosticCode.DelegateInvocation);
            var c0419 = diagnostics.Count(d => d.Code == DiagnosticCode.AssumedEffects
                && d.Message.Contains("function-typed value", StringComparison.Ordinal));
            if (c0418 > 0 || c0419 > 0)
                perFile.Add(new DAFileEntry(rel, c0418, c0419));
        }

        return new DALedger(
            MeasuredCommit: null,
            FileCount: files.Count,
            FilesNotReachingEffectPass: notReachingEffectPass.Count,
            NotReachingEffectPassNote: NotReachingEffectPassNote,
            NotReachingEffectPass: notReachingEffectPass,
            CompileExceptions: exceptions,
            Calor0418: perFile.Sum(f => f.Calor0418),
            Calor0419FunctionTyped: perFile.Sum(f => f.Calor0419FunctionTyped),
            Total: perFile.Sum(f => f.Calor0418 + f.Calor0419FunctionTyped),
            PerFile: perFile);
    }

    private static List<string> EnumerateCalorFiles(string root)
    {
        // Filter on REPO-RELATIVE paths: the checkout itself may live under a
        // directory named like one of the excluded segments (e.g. a worktree under
        // `.claude/worktrees/`), and an absolute-path filter would then match every file.
        //
        // Every dot-directory is skipped, not just `.git` / `.claude`: this is a
        // FILESYSTEM walk, not `git ls-files`, and the agent-native harness leaves
        // gitignored `.prev-src/` and `.envelope-src/` copies of every run's source
        // under `bench/phase0-agent-native/epochs/**/` (PP-E1 leg B, PR #1110: a tree
        // with them present counted 1006 files where a clean checkout counts 926 —
        // 927 since #1104's crash-repro fixture).
        // No committed .calr lives under a dot-directory, so the rule changes nothing
        // on a clean tree; it only stops a local regeneration from freezing a
        // denominator CI can never reproduce.
        return Directory.EnumerateFiles(root, "*.calr", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(rel =>
            {
                var segments = rel.Split('/');
                var directories = segments.Take(segments.Length - 1).ToList();
                return !directories.Any(d => d is "bin" or "obj" or "node_modules" || d.StartsWith('.'))
                    && !rel.StartsWith("bench/corpus/", StringComparison.Ordinal)
                    // Design-spike ARTIFACTS are not corpus. Round 3 moved the
                    // harness's scratch .calr outside the repository for exactly
                    // this reason; the emitter spike's before/after fixtures are
                    // committed on purpose, so they cannot be moved and must be
                    // excluded by path instead. The ledger's counts are unchanged
                    // by this line — these files were never part of the 886.
                    && !rel.StartsWith("docs/design/spikes/", StringComparison.Ordinal);
            })
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => Path.Combine(root, f))
            .ToList();
    }

    // ---------------------------------------------------------------- D-B

    [SkippableFact]
    public void DB_CSharpBackstop_MatchesLedgerExactly()
    {
        var root = RepoRoot();
        var srcRoots = Subjects
            .Select(s => Path.Combine(root, s.SubmodulePath, "src"))
            .ToList();
        Skip.IfNot(srcRoots.All(Directory.Exists), "corpus submodules not initialized");

        var measured = MeasureDB(root);
        Assert.True(measured.Aggregate.Total > 0,
            "D-B produced zero sites across all three subjects — corpus detection broke.");

        if (Regenerate())
        {
            WriteLedger(existing => existing with { DB = measured with { MeasuredCommit = HeadSha() } });
            return;
        }

        var ledger = ReadLedger();
        Assert.NotNull(ledger.DB);
        var recorded = ledger.DB!;
        for (var i = 0; i < Subjects.Length; i++)
        {
            Assert.True(i < recorded.PerSubject.Count, $"ledger dB.perSubject lacks {Subjects[i].Name}");
            Assert.Equal(recorded.PerSubject[i].Subject, measured.PerSubject[i].Subject);
            Assert.True(recorded.PerSubject[i].Sha == measured.PerSubject[i].Sha,
                $"{Subjects[i].Name} submodule moved from {recorded.PerSubject[i].Sha} to " +
                $"{measured.PerSubject[i].Sha} — a corpus bump regenerates the ledger IN THIS PR.");
            Assert.Equal(recorded.PerSubject[i], measured.PerSubject[i]);
        }
        Assert.Equal(recorded.PerSubject.Count, measured.PerSubject.Count);
        Assert.Equal(recorded.Aggregate, measured.Aggregate);
    }

    private static DBLedger MeasureDB(string root)
    {
        var perSubject = new List<DBSubjectEntry>();
        foreach (var (name, submodulePath) in Subjects)
        {
            var srcRoot = Path.Combine(root, submodulePath, "src");
            var counts = new DBCounts();
            var files = Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(srcRoot, f).Replace('\\', '/'))
                .Where(rel =>
                {
                    var segments = rel.Split('/');
                    return !segments.Take(segments.Length - 1).Any(d => d is "bin" or "obj");
                })
                .OrderBy(f => f, StringComparer.Ordinal)
                .Select(rel => Path.Combine(srcRoot, rel))
                .ToList();

            // Parse once; collect delegate declarations across the WHOLE subject first so
            // a delegate declared in one file (MediatR's RequestHandlerDelegate<T> in
            // IPipelineBehavior.cs — the middleware/`next` combinator §4.1 registers) counts
            // where it is used in another (Pipeline/*.cs).
            var roots = files.Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), ParseOptions).GetRoot()).ToList();
            var subjectDelegateTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rootNode in roots)
                foreach (var del in rootNode.DescendantNodes().OfType<DelegateDeclarationSyntax>())
                {
                    counts.DelegateDeclarations++;
                    subjectDelegateTypes.Add(del.Identifier.ValueText);
                }
            foreach (var rootNode in roots)
                CountFile(rootNode, subjectDelegateTypes, counts);

            perSubject.Add(new DBSubjectEntry(
                Subject: name,
                Sha: GitLinkSha(root, submodulePath),
                FilesScanned: files.Count,
                Lambdas: counts.Lambdas,
                AnonymousMethods: counts.AnonymousMethods,
                DelegateDeclarations: counts.DelegateDeclarations,
                DelegateTypedDeclarations: counts.DelegateTypedDeclarations,
                DelegateInvocations: counts.DelegateInvocations,
                Total: counts.Total));
        }

        var aggregate = new DBAggregate(
            FilesScanned: perSubject.Sum(s => s.FilesScanned),
            Lambdas: perSubject.Sum(s => s.Lambdas),
            AnonymousMethods: perSubject.Sum(s => s.AnonymousMethods),
            DelegateDeclarations: perSubject.Sum(s => s.DelegateDeclarations),
            DelegateTypedDeclarations: perSubject.Sum(s => s.DelegateTypedDeclarations),
            DelegateInvocations: perSubject.Sum(s => s.DelegateInvocations),
            Total: perSubject.Sum(s => s.Total));
        return new DBLedger(MeasuredCommit: null, perSubject, aggregate);
    }

    private static readonly CSharpParseOptions ParseOptions = new(
        LanguageVersion.Preview,
        DocumentationMode.Parse,
        SourceCodeKind.Regular,
        preprocessorSymbols: Array.Empty<string>());

    private sealed class DBCounts
    {
        public int Lambdas, AnonymousMethods, DelegateDeclarations,
            DelegateTypedDeclarations, DelegateInvocations;
        public int Total => Lambdas + AnonymousMethods + DelegateDeclarations
            + DelegateTypedDeclarations + DelegateInvocations;
    }

    private static void CountFile(SyntaxNode rootNode, HashSet<string> subjectDelegateTypes, DBCounts counts)
    {
        var nodes = rootNode.DescendantNodes().ToList();

        counts.Lambdas += nodes.Count(n => n is SimpleLambdaExpressionSyntax
            || n is ParenthesizedLambdaExpressionSyntax);
        counts.AnonymousMethods += nodes.OfType<AnonymousMethodExpressionSyntax>().Count();

        bool IsDelegateType(TypeSyntax? type)
        {
            if (type is NullableTypeSyntax nullable) type = nullable.ElementType;
            if (type is QualifiedNameSyntax qualified) type = qualified.Right;
            var identifier = type switch
            {
                GenericNameSyntax g => g.Identifier.ValueText,
                IdentifierNameSyntax i => i.Identifier.ValueText,
                _ => null,
            };
            return identifier != null
                && (BclDelegateTypeNames.Contains(identifier) || subjectDelegateTypes.Contains(identifier));
        }

        var declaredNames = new HashSet<string>(StringComparer.Ordinal);
        void Declared(string name)
        {
            counts.DelegateTypedDeclarations++;
            declaredNames.Add(name);
        }

        foreach (var p in nodes.OfType<ParameterSyntax>())
            if (IsDelegateType(p.Type)) Declared(p.Identifier.ValueText);
        // BaseFieldDeclarationSyntax covers both `Func<T> f;` (FieldDeclarationSyntax) and
        // `event EventHandler E;` (EventFieldDeclarationSyntax, which is NOT a FieldDeclaration).
        foreach (var f in nodes.OfType<BaseFieldDeclarationSyntax>())
            if (IsDelegateType(f.Declaration.Type))
                foreach (var v in f.Declaration.Variables) Declared(v.Identifier.ValueText);
        foreach (var prop in nodes.OfType<PropertyDeclarationSyntax>())
            if (IsDelegateType(prop.Type)) Declared(prop.Identifier.ValueText);
        foreach (var local in nodes.OfType<LocalDeclarationStatementSyntax>())
            if (IsDelegateType(local.Declaration.Type))
                foreach (var v in local.Declaration.Variables) Declared(v.Identifier.ValueText);

        foreach (var invocation in nodes.OfType<InvocationExpressionSyntax>())
        {
            var target = invocation.Expression;
            string? name = target switch
            {
                IdentifierNameSyntax id => id.Identifier.ValueText,
                MemberAccessExpressionSyntax
                {
                    Name.Identifier.ValueText: "Invoke",
                    Expression: IdentifierNameSyntax receiver
                } => receiver.Identifier.ValueText,
                _ => null,
            };
            // `name?.Invoke(...)`: the invocation's expression is `.Invoke` under a
            // ConditionalAccessExpression whose receiver is the identifier.
            if (name == null
                && target is MemberBindingExpressionSyntax { Name.Identifier.ValueText: "Invoke" }
                && invocation.Parent is ConditionalAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax conditionalReceiver
                })
            {
                name = conditionalReceiver.Identifier.ValueText;
            }
            if (name != null && declaredNames.Contains(name))
                counts.DelegateInvocations++;
        }
    }

    private static string GitLinkSha(string root, string submodulePath) =>
        Git(root, $"rev-parse HEAD:{submodulePath}", $"pinned gitlink for {submodulePath}");

    // ------------------------------------------------------------- shape

    [Fact]
    public void Ledger_DeclaresFloor25_AndMeasuredCommitIsFullSha()
    {
        var ledger = ReadLedger();
        Assert.Equal(1, ledger.SchemaVersion);
        Assert.Equal(RegisteredAt, ledger.RegisteredAt);
        Assert.Equal(Floor, ledger.Floor);
        Assert.Equal(FloorRule, ledger.FloorRule);
        Assert.True(IsSha(ledger.MeasuredCommit),
            $"measuredCommit must be a 40-hex commit SHA, was '{ledger.MeasuredCommit}'");
        Assert.Equal(ScopeText, ledger.Scope);
        Assert.Equal(Classes.OrderBy(kv => kv.Key, StringComparer.Ordinal),
            ledger.Classes.OrderBy(kv => kv.Key, StringComparer.Ordinal));
        Assert.NotNull(ledger.DA);
        Assert.NotNull(ledger.DB);
        // Both legs must have been measured at the SAME commit, and the top-level SHA is
        // that commit — a regeneration that touched only one leg (e.g. D-B skipped for
        // missing submodules) leaves the SHAs disagreeing and fails here.
        Assert.True(ledger.DA!.MeasuredCommit == ledger.MeasuredCommit
                && ledger.DB!.MeasuredCommit == ledger.MeasuredCommit,
            $"Ledger legs were measured at different commits (top {ledger.MeasuredCommit}, " +
            $"dA {ledger.DA.MeasuredCommit}, dB {ledger.DB!.MeasuredCommit}) — regenerate BOTH " +
            "legs in one run with the corpus submodules initialized.");
        Assert.Equal(NotReachingEffectPassNote, ledger.DA.NotReachingEffectPassNote);
        // The floor is adjudicated on the SUM of the two denominators (§4.1).
        Assert.Equal(ledger.DA.Total + ledger.DB.Aggregate.Total, ledger.DemandTotal);
        Assert.True(ledger.DemandTotal >= ledger.Floor,
            $"demandTotal {ledger.DemandTotal} is below the {ledger.Floor}-site floor: gate 2 " +
            "would read not-adjudicated — the registration must say so explicitly.");
    }

    private static bool IsSha(string? value) =>
        value != null && Regex.IsMatch(value, "^[0-9a-f]{40}$");

    // ------------------------------------------------------------ ledger I/O

    private static Ledger ReadLedger()
    {
        Assert.True(File.Exists(LedgerPath()),
            $"Ledger missing at {LedgerPath()} — run once with {RegenerateEnvVar}=1 (submodules initialized).");
        return JsonSerializer.Deserialize<Ledger>(File.ReadAllText(LedgerPath()), JsonOptions)!;
    }

    /// <summary>
    /// Rewrites one leg, preserving the other — the two legs run in nondeterministic
    /// order under one invocation (BinderIncompleteRatchetTests review minor 5). Each
    /// leg carries the HEAD it was measured at; the top-level <c>measuredCommit</c> is
    /// advanced only when both legs agree, so a one-legged regeneration cannot
    /// re-stamp the ledger as freshly measured. <c>registeredAt</c> never moves.
    /// </summary>
    private static void WriteLedger(Func<Ledger, Ledger> update)
    {
        var existing = File.Exists(LedgerPath())
            ? JsonSerializer.Deserialize<Ledger>(File.ReadAllText(LedgerPath()), JsonOptions)!
            : new Ledger(1, RegisteredAt, null, Floor, FloorRule, ScopeText,
                new Dictionary<string, string>(Classes), 0, null, null);
        var updated = update(existing) with
        {
            SchemaVersion = 1,
            RegisteredAt = existing.RegisteredAt ?? RegisteredAt,
            Floor = Floor,
            FloorRule = FloorRule,
            Scope = ScopeText,
            Classes = new Dictionary<string, string>(Classes),
        };
        var legsAgree = updated.DA?.MeasuredCommit != null
            && updated.DA.MeasuredCommit == updated.DB?.MeasuredCommit;
        updated = updated with
        {
            MeasuredCommit = legsAgree ? updated.DA!.MeasuredCommit : existing.MeasuredCommit,
            DemandTotal = (updated.DA?.Total ?? 0) + (updated.DB?.Aggregate.Total ?? 0),
        };
        File.WriteAllText(LedgerPath(), JsonSerializer.Serialize(updated, JsonOptions) + "\n");
    }

    private static string HeadSha() => Git(RepoRoot(), "rev-parse HEAD", "HEAD");

    private static string Git(string root, string arguments, string what)
    {
        var psi = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0 && IsSha(output),
            $"could not resolve {what} via `git {arguments}`: '{output}'");
        return output;
    }

    // ------------------------------------------------------------ schema

    private sealed record Ledger(
        int SchemaVersion,
        string? RegisteredAt,
        string? MeasuredCommit,
        int Floor,
        string? FloorRule,
        string? Scope,
        Dictionary<string, string> Classes,
        int DemandTotal,
        [property: JsonPropertyName("dA")] DALedger? DA,
        [property: JsonPropertyName("dB")] DBLedger? DB);

    private sealed record DALedger(
        string? MeasuredCommit,
        int FileCount,
        int FilesNotReachingEffectPass,
        string? NotReachingEffectPassNote,
        List<DANotReachingEntry> NotReachingEffectPass,
        List<string> CompileExceptions,
        int Calor0418,
        int Calor0419FunctionTyped,
        int Total,
        List<DAFileEntry> PerFile);

    private sealed record DANotReachingEntry(string File, string FirstError);

    private sealed record DAFileEntry(string File, int Calor0418, int Calor0419FunctionTyped);

    private sealed record DBLedger(
        string? MeasuredCommit,
        List<DBSubjectEntry> PerSubject,
        DBAggregate Aggregate);

    private sealed record DBSubjectEntry(
        string Subject,
        string Sha,
        int FilesScanned,
        int Lambdas,
        int AnonymousMethods,
        int DelegateDeclarations,
        int DelegateTypedDeclarations,
        int DelegateInvocations,
        int Total);

    private sealed record DBAggregate(
        int FilesScanned,
        int Lambdas,
        int AnonymousMethods,
        int DelegateDeclarations,
        int DelegateTypedDeclarations,
        int DelegateInvocations,
        int Total);
}
