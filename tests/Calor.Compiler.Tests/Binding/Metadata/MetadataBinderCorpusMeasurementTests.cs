using Calor.Compiler.Binding.Metadata;
using Calor.Compiler.CodeGen;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using Xunit.Sdk;

namespace Calor.Compiler.Tests;

/// <summary>
/// v0.14 §S5 — resolved-fraction measurement over the three A-1.5.3-pinned
/// conversion subjects (MediatR <c>fb309026</c>, Serilog <c>0597ddfb</c>,
/// FluentValidation <c>71b3c60c</c>). Produces the per-subject and aggregate
/// numbers that §3.5.2's bar freezes at.
///
/// <para>Denominator: all invocations in each subject's source that target
/// a BCL type in <see cref="MetadataAllowedNamePrefixes"/>. Numerator:
/// invocations that <see cref="MetadataBinder"/> resolves back to the same
/// <see cref="IMethodSymbol"/> (by ToDisplayString match) that the corpus's
/// own SemanticModel identified.</para>
///
/// <para>Excluded from the denominator: (a) invocations whose target is in
/// the corpus itself or in a NuGet dependency outside our whitelist (Calor
/// programs will not bind against those), (b) invocations whose semantic
/// resolution failed in the corpus's own model (broken source).</para>
///
/// <para>Skipped when submodules are not initialized — the CI job's
/// <c>submodules: recursive</c> checkout ensures they are; local runs
/// without <c>git submodule update</c> get a
/// <see cref="SkipException"/>.</para>
/// </summary>
public class MetadataBinderCorpusMeasurementTests
{
    private static readonly string RepoRoot = CliTestHarness.FindRepoRoot();

    [SkippableFact]
    public void ResolvedFraction_OverPinnedCorpus_Measured()
    {
        var subjects = new (string Name, string SubmodulePath, string SrcSubpath)[]
        {
            ("MediatR",          "bench/corpus/MediatR",          "src"),
            ("Serilog",          "bench/corpus/serilog",          "src"),
            ("FluentValidation", "bench/corpus/FluentValidation", "src"),
        };

        var perSubject = new List<CorpusSubjectResult>();
        using var ctx = MetadataContext.Create();
        var binder = new MetadataBinder(ctx);

        foreach (var subject in subjects)
        {
            var srcRoot = Path.Combine(RepoRoot, subject.SubmodulePath, subject.SrcSubpath);
            Skip.IfNot(Directory.Exists(srcRoot),
                $"Corpus submodule '{subject.Name}' not initialized at {srcRoot}. " +
                "Run `git submodule update --init --depth=1 " + subject.SubmodulePath + "` first.");

            var result = MeasureSubject(subject.Name, srcRoot, binder);
            perSubject.Add(result);
            Console.WriteLine(
                $"S5-corpus {subject.Name}: {result.ResolvedCount} / {result.CandidateCount} " +
                $"({(result.CandidateCount == 0 ? 0.0 : (double)result.ResolvedCount / result.CandidateCount * 100):F1}%)");
        }

        var totalResolved = perSubject.Sum(r => r.ResolvedCount);
        var totalCandidates = perSubject.Sum(r => r.CandidateCount);
        var aggregatePct = totalCandidates == 0 ? 0.0 : (double)totalResolved / totalCandidates * 100;

        Console.WriteLine($"S5-corpus aggregate: {totalResolved} / {totalCandidates} ({aggregatePct:F1}%)");
        Console.WriteLine($"Metadata-binding corpus resolved-fraction: {aggregatePct:F1}%");

        // §S5 ratchet — the committed ledger is the floor. Regenerate via
        // the CALOR_REGENERATE_S5_LEDGER=1 env var when landing intended
        // changes (S6+ improvements, corpus SHA bumps).
        var ledgerPath = Path.Combine(RepoRoot, "bench", "phase0-agent-native",
            "metadata-binding-corpus-ledger.json");
        var regenerate = string.Equals(
            Environment.GetEnvironmentVariable("CALOR_REGENERATE_S5_LEDGER"),
            "1", StringComparison.Ordinal);

        if (regenerate || !File.Exists(ledgerPath))
        {
            var ledger = BuildLedger(perSubject, totalResolved, totalCandidates, aggregatePct);
            var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(ledgerPath, System.Text.Json.JsonSerializer.Serialize(ledger, opts));
            Console.WriteLine($"S5-corpus ledger regenerated: {ledgerPath}");
        }
        else
        {
            // V-1 gap #35 fix: round-2 m4's anti-tautology pin. Strict
            // per-subject and aggregate equality against the committed
            // ledger — the current measurement (recomputed live) must match
            // the ledger's numbers exactly. Prevents a scenario where a PR
            // silently hardcodes a stale number in the ledger without a
            // corresponding measurement update.
            using var stream = File.OpenRead(ledgerPath);
            var committed = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(stream);

            var ledgerAggResolved = committed.GetProperty("aggregateResolved").GetInt32();
            var ledgerAggCandidates = committed.GetProperty("aggregateCandidates").GetInt32();
            Assert.Equal(ledgerAggResolved, totalResolved);
            Assert.Equal(ledgerAggCandidates, totalCandidates);

            var ledgerSubjects = committed.GetProperty("perSubject");
            for (var i = 0; i < perSubject.Count; i++)
            {
                var ledgerSubject = ledgerSubjects[i];
                var live = perSubject[i];
                Assert.Equal(ledgerSubject.GetProperty("subject").GetString(), live.SubjectName);
                Assert.Equal(ledgerSubject.GetProperty("resolvedCount").GetInt32(), live.ResolvedCount);
                Assert.Equal(ledgerSubject.GetProperty("candidateCount").GetInt32(), live.CandidateCount);
            }
        }

        Assert.True(totalCandidates > 0,
            "S5 measurement produced zero candidates across all three subjects. " +
            "Submodules likely uninitialized or corpus source detection broke.");
    }

    private static object BuildLedger(
        List<CorpusSubjectResult> perSubject, int totalResolved, int totalCandidates, double aggregatePct) =>
        new
        {
            schemaVersion = 1,
            aggregateResolvedFraction = Math.Round(aggregatePct, 2),
            aggregateResolved = totalResolved,
            aggregateCandidates = totalCandidates,
            perSubject = perSubject.Select(r => new
            {
                subject = r.SubjectName,
                sha = r.SubjectSha,
                candidateCount = r.CandidateCount,
                resolvedCount = r.ResolvedCount,
                resolvedFraction = r.CandidateCount == 0
                    ? 0.0
                    : Math.Round((double)r.ResolvedCount / r.CandidateCount * 100, 2),
                unresolvedByClass = r.UnresolvedByClass,
                filesScanned = r.FilesScanned,
                filesSkipped = r.FilesSkipped,
            }).ToArray(),
        };

    private static CorpusSubjectResult MeasureSubject(string name, string srcRoot, MetadataBinder binder)
    {
        var candidateCount = 0;
        var resolvedCount = 0;
        var unresolvedByClass = new Dictionary<string, int>();
        var filesScanned = 0;
        var filesSkipped = 0;

        // Build a Roslyn compilation for the corpus using the same TPA-derived
        // references as MetadataContext. Missing NuGet packages produce
        // per-invocation resolution failures which we categorize as
        // "corpus-side unresolved" and exclude from the denominator.
        var files = Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToArray();

        var syntaxTrees = new List<SyntaxTree>();
        foreach (var file in files)
        {
            try
            {
                var source = File.ReadAllText(file);
                syntaxTrees.Add(CSharpSyntaxTree.ParseText(source, path: file));
                filesScanned++;
            }
            catch
            {
                filesSkipped++;
            }
        }

        var compilation = CSharpCompilation.Create(
            $"CorpusMeasurement_{name}",
            syntaxTrees,
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        foreach (var tree in syntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var invocations = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>();
            foreach (var invocation in invocations)
            {
                var info = model.GetSymbolInfo(invocation);
                if (info.Symbol is not IMethodSymbol target)
                {
                    continue;
                }

                var containingAssembly = target.ContainingAssembly?.Name;
                if (containingAssembly is null ||
                    !MetadataAllowedNamePrefixes.IsAllowed(containingAssembly))
                {
                    continue;
                }

                candidateCount++;

                var receiverType = target.IsStatic || target.IsExtensionMethod
                    ? target.ContainingType
                    : ExtractReceiverType(invocation, model) ?? target.ContainingType;

                if (receiverType is null) { Tally(unresolvedByClass, "NoReceiverType"); continue; }

                var argTypes = target.Parameters.Select(p =>
                    new MetadataArgument(p.Type, p.RefKind)).ToArray();

                var result = binder.ResolveCall(receiverType, target.Name, argTypes);
                if (result.IsResolved &&
                    SymbolEqualityComparer.Default.Equals(result.Symbol!.OriginalDefinition, target.OriginalDefinition))
                {
                    resolvedCount++;
                }
                else if (result.IsResolved)
                {
                    Tally(unresolvedByClass, "ResolvedButDifferentSymbol");
                }
                else
                {
                    Tally(unresolvedByClass, result.UnresolvedReason ?? "Unknown");
                }
            }
        }

        return new CorpusSubjectResult(
            SubjectName: name,
            SubjectSha: GetSubmoduleSha(name),
            CandidateCount: candidateCount,
            ResolvedCount: resolvedCount,
            UnresolvedByClass: unresolvedByClass,
            FilesScanned: filesScanned,
            FilesSkipped: filesSkipped);
    }

    private static ITypeSymbol? ExtractReceiverType(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            return model.GetTypeInfo(memberAccess.Expression).Type;
        }
        return null;
    }

    private static void Tally(Dictionary<string, int> byClass, string reason)
    {
        // Trim long unresolved reasons to a class prefix to keep the ledger tidy.
        var key = reason.Length > 60 ? reason[..60] + "…" : reason;
        byClass[key] = byClass.TryGetValue(key, out var c) ? c + 1 : 1;
    }

    private static string GetSubmoduleSha(string name) => name switch
    {
        "MediatR" => "fb309026775ef953a64fb5339d074426c1ad2c37",
        "Serilog" => "0597ddfbd4ec594d9c42edd745fe728a2198bad9",
        "FluentValidation" => "71b3c60cb5a16e02cb7957e478ec3fb6b983a73c",
        _ => "unknown",
    };

    private sealed record CorpusSubjectResult(
        string SubjectName,
        string SubjectSha,
        int CandidateCount,
        int ResolvedCount,
        Dictionary<string, int> UnresolvedByClass,
        int FilesScanned,
        int FilesSkipped);
}
