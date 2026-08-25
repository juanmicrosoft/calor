using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Calor.Compiler.Diagnostics;
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
/// --filter HigherOrderDemandLedger</c> (with submodules initialized so both
/// legs rewrite their section).</para>
///
/// <para>D-A runs on every shard (no submodules). D-B skips only where the
/// corpus submodules are absent — the <c>test</c> job and the
/// <c>compiler</c> shard check them out, so the equality is enforced in CI.</para>
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
        "D-A: every .calr under the repository root (bin/, obj/, .git/, node_modules/ and "
        + "bench/corpus/ submodules excluded; nothing else filtered), compiled one file at a time "
        + "via Program.Compile with default CompilationOptions (EnforceEffects=true, "
        + "UnknownCallPolicy.Strict, StrictEffects=false, no manifests, UnsafeTranspileOnly to "
        + "skip Roslyn validation of the emitted C#). Files that fail before the effect pass "
        + "(lexer/parser/type-check/binder errors; witnessed by the absence of the verbose "
        + "'Effect enforcement completed' status line) are counted in "
        + "filesNotReachingEffectPass, never dropped. Calor0419 is counted per DIAGNOSTIC "
        + "(one per function, first three reasons "
        + "shown), so a function with more than three assumptions whose function-typed reason "
        + "is truncated out of the message is under-counted — a known floor, not a ceiling. "
        + "D-B: Roslyn syntax-only (CSharpSyntaxTree.ParseText, LanguageVersion.Preview, no "
        + "preprocessor symbols defined, so inactive #if branches are not scanned) over "
        + "bench/corpus/{MediatR,serilog,FluentValidation}/src/**/*.cs (bin/, obj/ excluded).";

    /// <summary>
    /// The D-B classification, pinned as data: identifiers a declared type syntax
    /// must resolve to (after unwrapping <c>?</c> and a namespace qualifier) to count
    /// as delegate-typed. Same-file <c>delegate</c> declarations are added per file.
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
                "Parameters, fields, properties and locals whose declared type syntax "
                + "(after unwrapping '?' and a namespace qualifier) is Func<…>, Action, "
                + "Action<…>, Predicate<…>, Comparison<…>, Converter<…>, EventHandler, "
                + "EventHandler<…>, or the name of a delegate declared in the same file. "
                + "'var' locals never count.",
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
            WriteLedger(existing => existing with { DA = measured });
            return;
        }

        var ledger = ReadLedger();
        Assert.NotNull(ledger.DA);
        var recorded = ledger.DA!;

        Assert.True(recorded.FileCount == measured.FileCount,
            $"D-A corpus size moved: {measured.FileCount} .calr files vs ledger {recorded.FileCount}. " +
            $"Corpus additions/removals regenerate the ledger IN THIS PR ({RegenerateEnvVar}=1) " +
            "with the change named — never silently.");
        // Files that never reach the pass are pinned BY NAME: a parser/binder regression that
        // silently removes files from the effective denominator fails here, naming them.
        Assert.True(recorded.NotReachingEffectPass.SequenceEqual(measured.NotReachingEffectPass),
            "D-A files failing before the effect pass moved — a parser/binder change shrank or " +
            "grew the effective denominator; regenerate with the change named.\n  newly failing: " +
            string.Join(", ", measured.NotReachingEffectPass.Except(recorded.NotReachingEffectPass)) +
            "\n  newly reaching: " +
            string.Join(", ", recorded.NotReachingEffectPass.Except(measured.NotReachingEffectPass)));
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

        var perFile = new List<DAFileEntry>();
        var exceptions = new List<string>();
        var notReachingEffectPass = new List<string>();
        foreach (var file in files)
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            CompilationResult result;
            var status = new StringWriter();
            try
            {
                var options = new CompilationOptions
                {
                    UnsafeTranspileOnly = true,
                    Verbose = true,
                    StatusWriter = status,
                };
                Assert.True(options.EnforceEffects, "default CompilationOptions must enforce effects");
                Assert.Equal(Effects.UnknownCallPolicy.Strict, options.UnknownCallPolicy);
                result = Program.Compile(File.ReadAllText(file).Replace("\r\n", "\n"), file, options);
            }
            catch (Exception ex)
            {
                exceptions.Add($"{rel}: {ex.GetType().Name}");
                continue;
            }

            // The effect pass only runs once lexing, parsing, type checking, pattern/bind/
            // return validation and semantic binding are clean. Program.Compile's verbose
            // status line is the exact witness that the pass ran (diagnostic code bands
            // overlap across phases, so they are not). A file that dies earlier contributes
            // zero sites and is COUNTED as such rather than dropped from the denominator.
            if (!status.ToString().Contains("Effect enforcement completed", StringComparison.Ordinal))
            {
                notReachingEffectPass.Add(rel);
                continue;
            }

            var diagnostics = result.Diagnostics.ToList();

            var c0418 = diagnostics.Count(d => d.Code == DiagnosticCode.DelegateInvocation);
            var c0419 = diagnostics.Count(d => d.Code == DiagnosticCode.AssumedEffects
                && d.Message.Contains("function-typed value", StringComparison.Ordinal));
            if (c0418 > 0 || c0419 > 0)
                perFile.Add(new DAFileEntry(rel, c0418, c0419));
        }

        return new DALedger(
            FileCount: files.Count,
            FilesNotReachingEffectPass: notReachingEffectPass.Count,
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
        return Directory.EnumerateFiles(root, "*.calr", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(rel =>
            {
                var segments = rel.Split('/');
                var directories = segments.Take(segments.Length - 1).ToList();
                return !directories.Any(d => d is "bin" or "obj" or ".git" or ".claude" or "node_modules")
                    && !rel.StartsWith("bench/corpus/", StringComparison.Ordinal);
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
            WriteLedger(existing => existing with { DB = measured });
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
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();
            foreach (var file in files)
                CountFile(File.ReadAllText(file), counts);

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
        return new DBLedger(perSubject, aggregate);
    }

    private sealed class DBCounts
    {
        public int Lambdas, AnonymousMethods, DelegateDeclarations,
            DelegateTypedDeclarations, DelegateInvocations;
        public int Total => Lambdas + AnonymousMethods + DelegateDeclarations
            + DelegateTypedDeclarations + DelegateInvocations;
    }

    private static void CountFile(string source, DBCounts counts)
    {
        var parseOptions = new CSharpParseOptions(
            LanguageVersion.Preview,
            DocumentationMode.Parse,
            SourceCodeKind.Regular,
            preprocessorSymbols: Array.Empty<string>());
        var rootNode = CSharpSyntaxTree.ParseText(source, parseOptions).GetRoot();
        var nodes = rootNode.DescendantNodes().ToList();

        var localDelegateTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var del in nodes.OfType<DelegateDeclarationSyntax>())
        {
            counts.DelegateDeclarations++;
            localDelegateTypes.Add(del.Identifier.ValueText);
        }

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
                && (BclDelegateTypeNames.Contains(identifier) || localDelegateTypes.Contains(identifier));
        }

        var declaredNames = new HashSet<string>(StringComparer.Ordinal);
        void Declared(string name)
        {
            counts.DelegateTypedDeclarations++;
            declaredNames.Add(name);
        }

        foreach (var p in nodes.OfType<ParameterSyntax>())
            if (IsDelegateType(p.Type)) Declared(p.Identifier.ValueText);
        foreach (var f in nodes.OfType<FieldDeclarationSyntax>())
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

    private static string GitLinkSha(string root, string submodulePath)
    {
        var psi = new ProcessStartInfo("git", $"rev-parse HEAD:{submodulePath}")
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
            $"could not resolve the pinned gitlink for {submodulePath}: '{output}'");
        return output;
    }

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
        // The floor is adjudicated on the SUM of the two denominators (§4.1).
        Assert.Equal(ledger.DA!.Total + ledger.DB!.Aggregate.Total, ledger.DemandTotal);
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
    /// Rewrites one section, preserving the other — the two legs run in
    /// nondeterministic order under one invocation (BinderIncompleteRatchetTests
    /// review minor 5). <c>measuredCommit</c> is the HEAD the measurement ran at;
    /// <c>registeredAt</c> is the registration date and never moves.
    /// </summary>
    private static void WriteLedger(Func<Ledger, Ledger> update)
    {
        var existing = File.Exists(LedgerPath())
            ? JsonSerializer.Deserialize<Ledger>(File.ReadAllText(LedgerPath()), JsonOptions)!
            : new Ledger(1, RegisteredAt, HeadSha(), Floor, FloorRule, ScopeText,
                new Dictionary<string, string>(Classes), 0, null, null);
        var updated = update(existing) with
        {
            SchemaVersion = 1,
            RegisteredAt = existing.RegisteredAt ?? RegisteredAt,
            MeasuredCommit = HeadSha(),
            Floor = Floor,
            FloorRule = FloorRule,
            Scope = ScopeText,
            Classes = new Dictionary<string, string>(Classes),
        };
        updated = updated with
        {
            DemandTotal = (updated.DA?.Total ?? 0) + (updated.DB?.Aggregate.Total ?? 0),
        };
        File.WriteAllText(LedgerPath(), JsonSerializer.Serialize(updated, JsonOptions) + "\n");
    }

    private static string HeadSha()
    {
        var psi = new ProcessStartInfo("git", "rev-parse HEAD")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0 && IsSha(output), $"git rev-parse HEAD failed: '{output}'");
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
        int FileCount,
        int FilesNotReachingEffectPass,
        List<string> NotReachingEffectPass,
        List<string> CompileExceptions,
        int Calor0418,
        int Calor0419FunctionTyped,
        int Total,
        List<DAFileEntry> PerFile);

    private sealed record DAFileEntry(string File, int Calor0418, int Calor0419FunctionTyped);

    private sealed record DBLedger(List<DBSubjectEntry> PerSubject, DBAggregate Aggregate);

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
