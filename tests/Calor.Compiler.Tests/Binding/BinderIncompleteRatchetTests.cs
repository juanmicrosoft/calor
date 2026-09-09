using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

// Flat test namespace: see BinderDispatchCompletenessTests.
namespace Calor.Compiler.Tests;

/// <summary>
/// #762 B1: the incomplete-fraction instrument (freeze registration F-2 as amended
/// 2026-08-10). Parses and binds every `.calr` under the F-2 in-repo corpus roots and
/// counts Calor0259 (AnalysisIncomplete) against binder expression attempts.
/// The committed baseline is a RATCHET, and parse coverage is asserted too:
/// - the incomplete count may only move DOWN as family PRs land (update the baseline in
///   the same PR — an unrecorded improvement also fails, so the baseline tracks reality);
/// - routing a bound construct back to the fallback RAISES the count and fails here —
///   the F-2 discriminating pin, running on every test invocation;
/// - corpus additions may legitimately move counts, in which case the PR updates the
///   baseline with the added files named (the F-2 amendment's stated exception);
/// - a parse failure is allowed ONLY for files on the explicit list below — anything
///   else fails by NAME (review C2: without this, a parser regression removes files from
///   the denominator, the count "improves", and the failure message invites laundering
///   the regression into the baseline).
/// Regenerate: CALOR_UPDATE_BINDER_BASELINE=1 dotnet test --filter BinderIncompleteRatchet
/// BOTH F-2 legs are active as of B2: the conversion leg (three A-1.5.3 subjects at pinned
/// submodule commits, converted in-process) skips only where submodules are absent.
/// </summary>
public class BinderIncompleteRatchetTests
{
    private static readonly string[] CorpusRoots = ["samples", "tests", "benchmarks",
        Path.Combine("src", "Calor.Compiler", "Resources", "SelfTest")];

    /// <summary>
    /// The ONLY files allowed to fail parsing, each with its registered reason.
    /// Repo-relative, forward slashes. Deliberate error fixtures stay; the known-stale
    /// benchmark subjects are tracked by #901 and leave this list as they are repaired.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> AllowedParseFailures =
        new Dictionary<string, string>
        {
            // Deliberately-invalid lint fixtures (they exist to fail):
            ["tests/TestData/LintScenarios/10_error_cases/syntax_error.calr"] = "error fixture",
            ["tests/TestData/LintScenarios/10_error_cases/unterminated_string.calr"] = "error fixture",
            ["tests/TestData/LintScenarios/10_error_cases/mismatched_ids.calr"] =
                "error fixture (NOTE: currently fails on Calor0830 legacy closers, not the " +
                "mismatched ids it was built for — stale in its own way, see #901's pattern)",
            // Known-stale intended-valid benchmark subjects — #901, list shrinks as repaired:
            ["benchmarks/arithmetic/div-by-zero.calr"] = "#901 multi-generation stale",
            ["benchmarks/loops/bounds-violation.calr"] = "#901 multi-generation stale",
            ["benchmarks/null-safety/null-deref.calr"] = "#901 multi-generation stale",
            ["benchmarks/security/command-injection.calr"] = "#901 multi-generation stale",
            ["benchmarks/security/path-traversal.calr"] = "#901 multi-generation stale",
            ["benchmarks/security/sql-injection.calr"] = "#901 multi-generation stale",
        };

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Directory.Build.props")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }

    private static string BaselinePath() => Path.Combine(RepoRoot(),
        "bench", "phase0-agent-native", "binder-incomplete-baseline.json");

    // ONE scope string — the two regen writers previously hardcoded different texts,
    // making the committed file depend on writer order (review minor 5).
    private const string ScopeText =
        "in-repo F-2 plus selected-active conversion binding and preserve-all opaque coverage; "
        + "Roslyn-selected conversion uses genuinely empty default symbols with "
        + "C# Preview/regular/parse options; legacy source-order 18005 is informational. "
        + "#1189 preserves unchecked blocks in serilog/src/Serilog/Events/EventProperty.cs "
        + "and FluentValidation/src/FluentValidation/Internal/AccessorCache.cs: "
        + "previously unconverted files 2 -> 0, accounted opaque boundaries 89 -> 91 "
        + "and opaque expressions 6423 -> 6471; incomplete diagnostics remain zero. "
        + "#1191: RoslynSelectedAttempted counts binder visits, including generated "
        + "references and opaque expressions, NOT faithful native source expressions. "
        + "34942 -> 34734 -> 34703 is reconciled per file in binder-expression-attribution.json "
        + "(the last step preserves eager operand regions in 67d4c259); "
        + "expression interop is now included in opaque coverage. "
        + "binder-source-coverage.json separately pins source and representation identities; "
        + "the expanded opaque budget (+20 boundaries/+180 source expressions) was explicitly "
        + "accepted by the parent for #1191 after per-file evidence review, not as a native-fidelity claim. "
        + "Combined migration measurement compares accepted 7d218c59 with exact 46baabf8: "
        + "34703 -> 35179 binder visits; binder-combined-integration.json records the separate "
        + "additional 11 opaque boundaries/268 source expressions and four generated lambda carrier "
        + "renames. The additional opacity was separately accepted after parent per-site evidence "
        + "review; it is not folded into the earlier M1 budget or an independent-review claim";

    private static string SourceCoveragePath() => Path.Combine(RepoRoot(),
        "bench", "phase0-agent-native", "binder-source-coverage.json");

    private static readonly Lazy<IReadOnlyList<SourceCarrierCase>> SourceCarrierCases = new(() =>
        JsonSerializer.Deserialize<SourceCarrierEvidence>(File.ReadAllText(Path.Combine(RepoRoot(),
            "bench", "phase0-agent-native", "binder-source-carrier-evidence.json")))!.Cases);

    [Fact]
    public void InRepoCorpus_IncompleteCount_DoesNotExceedBaseline()
    {
        var root = RepoRoot();
        var files = CorpusRoots
            .Select(r => Path.Combine(root, r))
            .Where(Directory.Exists)
            .SelectMany(r => Directory.EnumerateFiles(r, "*.calr", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(files);

        int incomplete = 0, parsedFiles = 0, expressionsBound = 0;
        var parseFailures = new List<string>();
        foreach (var file in files)
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            var diagnostics = new DiagnosticBag();
            var lexer = new Lexer(File.ReadAllText(file).Replace("\r\n", "\n"), diagnostics);
            var parser = new Parser(lexer.TokenizeAllForParser(), diagnostics);
            var module = parser.Parse();
            if (diagnostics.HasErrors) { parseFailures.Add(rel); continue; }
            parsedFiles++;

            var bindBag = new DiagnosticBag();
            var binder = new Binder(bindBag);
            binder.Bind(module);
            expressionsBound += binder.ExpressionsBound;
            incomplete += bindBag.Count(d => d.Code == DiagnosticCode.AnalysisIncomplete);
        }

        // Parse failures outside the registered list fail BY NAME — never a silent
        // denominator shrink (review C2 / the F-2 anti-vacuity rule).
        var unexpected = parseFailures.Where(f => !AllowedParseFailures.ContainsKey(f)).ToList();
        Assert.True(unexpected.Count == 0,
            "Files failed to parse that are NOT on the registered allowed list — a parser " +
            "regression or an unregistered stale file; fix the parse or register with a " +
            $"reason and an issue:\n  {string.Join("\n  ", unexpected)}");
        var recovered = AllowedParseFailures.Keys.Except(parseFailures).ToList();
        Assert.True(recovered.Count == 0,
            "Files on the allowed-parse-failure list now PARSE — remove them from the list " +
            $"(and the F-2 amendment) in this PR:\n  {string.Join("\n  ", recovered)}");

        var measured = new Baseline(incomplete, parsedFiles, parseFailures.Count, expressionsBound,
            ScopeText);

        if (Environment.GetEnvironmentVariable("CALOR_UPDATE_BINDER_BASELINE") == "1")
        {
            // Preserve the conversion-leg section — the two regen writers run in
            // nondeterministic order under one test invocation.
            var existing = File.Exists(BaselinePath())
                ? JsonSerializer.Deserialize<Baseline>(File.ReadAllText(BaselinePath()))
                : null;
            File.WriteAllText(BaselinePath(), JsonSerializer.Serialize(
                measured with
                {
                    Conversion = existing?.Conversion,
                    PreserveCoverage = existing?.PreserveCoverage
                },
                new JsonSerializerOptions { WriteIndented = true }) + "\n");
            return;
        }

        Assert.True(File.Exists(BaselinePath()),
            "Baseline missing — run once with CALOR_UPDATE_BINDER_BASELINE=1");
        var baseline = JsonSerializer.Deserialize<Baseline>(File.ReadAllText(BaselinePath()))!;

        Assert.True(parsedFiles == baseline.ParsedFiles && parseFailures.Count == baseline.ParseFailures,
            $"Parse coverage moved: {parsedFiles} parsed/{parseFailures.Count} failed vs baseline " +
            $"{baseline.ParsedFiles}/{baseline.ParseFailures}. Corpus additions/repairs must " +
            "regenerate the baseline IN THIS PR with the change named — never silently.");
        Assert.True(incomplete <= baseline.IncompleteCount,
            $"RATCHET: incomplete count rose from {baseline.IncompleteCount} to {incomplete} " +
            $"({parsedFiles} files, {expressionsBound} expressions). A bound construct regressed " +
            "to the fallback, or new corpus files use unbound constructs — if the latter, update " +
            "the baseline in this PR with the added files named (F-2 amendment exception).");
        Assert.True(incomplete == baseline.IncompleteCount,
            $"Incomplete count IMPROVED from {baseline.IncompleteCount} to {incomplete} — " +
            "record it: regenerate the baseline in this PR so the ratchet tracks reality.");
    }

    [SkippableFact]
    public void ConversionLeg_IncompleteCount_MatchesBaseline()
    {
        var root = RepoRoot();
        var subjects = new[] { "MediatR", "serilog", "FluentValidation" }
            .Select(subject => Path.Combine(root, "bench", "corpus", subject, "src"))
            .ToList();
        Skip.IfNot(subjects.All(Directory.Exists), "corpus submodules not initialized");

        var files = subjects
            .SelectMany(directory => Directory.EnumerateFiles(
                directory, "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToArray();
        var preserveParseOptions = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
            Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview,
            Microsoft.CodeAnalysis.DocumentationMode.Parse,
            Microsoft.CodeAnalysis.SourceCodeKind.Regular,
            preprocessorSymbols: Array.Empty<string>());

        var native = new NativeConversionCoverage();
        var preserve = new PreserveConversionCoverage();
        var opaqueIdentities = new HashSet<string>(StringComparer.Ordinal);
        var unconvertedIdentities = new HashSet<string>(StringComparer.Ordinal);
        var sourceCoverage = new SortedDictionary<string, SourceCoverageRecord>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var source = File.ReadAllText(file).Replace("\r\n", "\n");
            MeasureNative(file, source, native);
            var relative = Path.GetRelativePath(Path.Combine(root, "bench", "corpus"), file)
                .Replace('\\', '/');
            sourceCoverage.Add(relative, native.LastSourceCoverage!);
            MeasurePreserve(
                file,
                source,
                preserveParseOptions,
                preserve,
                opaqueIdentities,
                unconvertedIdentities);
        }
        preserve.OpaqueIdentityCount =
            opaqueIdentities.Count;
        preserve.UnconvertedIdentityCount =
            unconvertedIdentities.Count;

        var measuredNative = native.ToRecord();
        var measuredPreserve = preserve.ToRecord();
        Assert.Equal(0, measuredPreserve.OpaqueUnmapped);
        Assert.Equal(measuredPreserve.OpaqueBoundaries, measuredPreserve.OpaqueIdentityCount);
        Assert.Equal(measuredPreserve.UnconvertedFiles, measuredPreserve.UnconvertedIdentityCount);
        Assert.Equal(files.Length, measuredNative.ConvertedAndBound);
        Assert.Equal(0, measuredNative.ConvertExceptions);
        Assert.Equal(0, measuredNative.EmptyOutput);
        Assert.Equal(0, measuredNative.OutputParseFailures);
        if (Environment.GetEnvironmentVariable("CALOR_UPDATE_BINDER_BASELINE") == "1")
        {
            var baseline = File.Exists(BaselinePath())
                ? JsonSerializer.Deserialize<Baseline>(File.ReadAllText(BaselinePath()))!
                : new Baseline(0, 0, 0, 0, ScopeText);
            File.WriteAllText(BaselinePath(), JsonSerializer.Serialize(
                baseline with
                {
                    Conversion = measuredNative,
                    PreserveCoverage = measuredPreserve,
                    Scope = ScopeText
                },
                new JsonSerializerOptions { WriteIndented = true }) + "\n");
            File.WriteAllText(SourceCoveragePath(),
                JsonSerializer.Serialize(sourceCoverage,
                    new JsonSerializerOptions { WriteIndented = true }) + "\n");
            return;
        }

        var recorded = JsonSerializer.Deserialize<Baseline>(
            File.ReadAllText(BaselinePath()))!;
        Assert.NotNull(recorded.Conversion);
        Assert.NotNull(recorded.PreserveCoverage);
        Assert.Equal(
            LegacySourceOrderAttempted,
            measuredNative.LegacySourceOrderAttempted);
        // Binder visits include lowering artifacts and opaque expressions. Exact equality
        // still catches drift; a monotonic lower bound would reward unnecessary temporaries.
        Assert.Equal(recorded.Conversion, measuredNative);
        var recordedSources = JsonSerializer.Deserialize<
            SortedDictionary<string, SourceCoverageRecord>>(
                File.ReadAllText(SourceCoveragePath()))!;
        Assert.Equal(recordedSources.Keys.Order(StringComparer.Ordinal), sourceCoverage.Keys);
        foreach (var (file, measured) in sourceCoverage)
            Assert.True(recordedSources[file] == measured,
                $"Source representation changed in {file}: {recordedSources[file]} -> {measured}. "
                + "Audit source identities and expression interop, not just the binder-visit total.");
        Assert.Equal(0, measuredPreserve.OpaqueUnmapped);
        Assert.Equal(
            measuredPreserve.OpaqueBoundaries,
            measuredPreserve.OpaqueIdentityCount);
        Assert.Equal(
            measuredPreserve.UnconvertedFiles,
            measuredPreserve.UnconvertedIdentityCount);
        Assert.True(
            measuredPreserve.OpaqueBoundaries <= recorded.PreserveCoverage.OpaqueBoundaries
            && measuredPreserve.OpaqueExpressions <= recorded.PreserveCoverage.OpaqueExpressions
            && measuredPreserve.UnconvertedFiles <= recorded.PreserveCoverage.UnconvertedFiles,
            $"Preserve-mode opaque coverage regressed: measured {measuredPreserve} "
            + $"vs baseline {recorded.PreserveCoverage}.");
        Assert.Equal(recorded.PreserveCoverage, measuredPreserve);
    }

    [Fact]
    public void ExpressionInterop_IsOpaqueAndSurvivesSerialization()
    {
        const string source = "class C { int F() => Call(1); }";
        var start = source.IndexOf("Call(1)", StringComparison.Ordinal);
        var expression = new RawCSharpExpressionNode(new TextSpan(start, 7, 1, start + 1), "Call(1)");
        var module = MeasurementModule(expression);
        var coverage = MeasureSourceCoverage(module,
            Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source),
            source, "expression.cs", 1);
        Assert.Equal(1, coverage.OpaqueBoundaries);
        Assert.Equal(3, coverage.OpaqueSourceExpressions); // Invocation, callee, argument.
        Assert.Equal(0, coverage.ExactExpressionSourceSpans);

        var calor = new Compiler.Migration.CalorEmitter().Emit(module);
        var diagnostics = new DiagnosticBag();
        var reparsed = new Parser(new Lexer(calor, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors);
        AssertOpaqueSerializationPreserved(module, reparsed, "expression.cs");
        Assert.Throws<Xunit.Sdk.TrueException>(() =>
            AssertOpaqueSerializationPreserved(module, MeasurementModule(), "dropped.cs"));
        Assert.Throws<Xunit.Sdk.TrueException>(() =>
            AssertOpaqueSerializationPreserved(module,
                MeasurementModule(new RawCSharpExpressionNode(expression.Span, "Call(2)")), "changed.cs"));
        Assert.Throws<Xunit.Sdk.TrueException>(() =>
            CollectOpaqueSpans(
                MeasurementModule(new RawCSharpExpressionNode(expression.Span, "Call(2)")),
                source, "misattributed.cs", new PreserveConversionCoverage()));
        Assert.Throws<Xunit.Sdk.TrueException>(() =>
            AssertOpaqueSerializationPreserved(
                MeasurementModule(new RawCSharpExpressionNode(default,
                    "F(\n#if A\n1\n#else\n2\n#endif\n)")),
                MeasurementModule(new RawCSharpExpressionNode(default,
                    "F(\n#if A\n3\n#else\n2\n#endif\n)")),
                "inactive-branch.cs"));
    }

    [Fact]
    public void SourceIdentityGuard_DetectsEqualCountSwapsAndNativeToOpaque()
    {
        const string source = "class C { int F() => 1 + 2; }";
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source);
        var literals = tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax>().ToArray();
        var first = literals[0].Span;
        var second = literals[1].Span;
        var firstSpan = new TextSpan(first.Start, first.Length, 1, first.Start + 1);
        var secondSpan = new TextSpan(second.Start, second.Length, 1, second.Start + 1);
        var before = MeasureSourceCoverage(MeasurementModule(new IntLiteralNode(firstSpan, 1)),
            tree, source, "identity.cs", 1);
        var swapped = MeasureSourceCoverage(MeasurementModule(new IntLiteralNode(secondSpan, 2)),
            tree, source, "identity.cs", 1);
        Assert.Equal(before.BinderAttempts, swapped.BinderAttempts);
        Assert.Equal(before.ExactExpressionSourceSpans, swapped.ExactExpressionSourceSpans);
        Assert.NotEqual(before.ExactExpressionSourceIdentityHash, swapped.ExactExpressionSourceIdentityHash);
        Assert.Equal(before.UnmappedSourceExpressions, swapped.UnmappedSourceExpressions);
        Assert.NotEqual(before.UnmappedSourceIdentityHash, swapped.UnmappedSourceIdentityHash);
        var opaque = MeasureSourceCoverage(MeasurementModule(new RawCSharpExpressionNode(firstSpan, "1")),
            tree, source, "identity.cs", 1);
        Assert.Equal(before.BinderAttempts, opaque.BinderAttempts);
        Assert.Equal(0, opaque.ExactExpressionSourceSpans);
        Assert.Equal(1, opaque.OpaqueSourceExpressions);
        Assert.NotEqual(before, opaque);
        var binarySpan = TextSpan.FromBounds(first.Start, second.End, 1, first.Start + 1);
        var mixed = MeasureSourceCoverage(MeasurementModule(
                new BinaryOperationNode(binarySpan, BinaryOperator.Add,
                    new RawCSharpExpressionNode(firstSpan, "1"), new IntLiteralNode(secondSpan, 2))),
            tree, source, "mixed.cs", 3);
        Assert.Equal(1, mixed.OpaqueSourceExpressions);
        Assert.Equal(2, mixed.ExactExpressionSourceSpans);
        Assert.Equal(1, mixed.ExactExpressionsWithOpaqueDescendants);
        Assert.Equal(mixed.SourceExpressions,
            mixed.ExactExpressionSourceSpans + mixed.OpaqueSourceExpressions + mixed.UnmappedSourceExpressions);

        var bindDiagnostics = new DiagnosticBag();
        new Binder(bindDiagnostics).Bind(MeasurementModule(new ReferenceNode(firstSpan, "missing")));
        var failedBinding = WithBindingCoverage(before, bindDiagnostics);
        Assert.True(failedBinding.BindingErrorCount > 0);
        Assert.NotEqual(WithBindingCoverage(before, new DiagnosticBag()), failedBinding);
        Assert.Equal(72, SourceCarrierCases.Value.Count);
        Assert.Equal(47, SourceCarrierCases.Value.Count(c => c.SourceKind == "GenericName"));
        Assert.Equal(25, SourceCarrierCases.Value.Count(c => c.SourceKind == "ConditionalAccessExpression"));
        Assert.Equal(72, SourceCarrierCases.Value
            .Select(c => (c.File, c.SourceStart, c.SourceEnd)).Distinct().Count());
        var carrier = new SourceCarrierCase("identity.cs", first.Start, first.End,
            "NumericLiteralExpression", "1", nameof(IntLiteralNode), first.Start, first.End, "1");
        AssertSourceCarrierPreserved([new IntLiteralNode(firstSpan, 1)], source, carrier);
        Assert.Throws<Xunit.Sdk.TrueException>(() =>
            AssertSourceCarrierPreserved([new IntLiteralNode(firstSpan, 2)], source, carrier));
    }

    [Theory]
    [InlineData("return gate && Check();")]
    [InlineData("return gate || Check();")]
    [InlineData("return gate ? Check() : false;")]
    public void NativeMeasurementControls_RejectInteropAndEmitterFallback(string body)
    {
        var conversion = new Compiler.Migration.CSharpToCalorConverter().Convert(
            "public static class C { static bool Check() => true; "
            + "public static bool Run(bool gate) { " + body + " } }");
        Assert.True(conversion.Success, string.Join("; ", conversion.Issues));
        Assert.DoesNotContain(conversion.Losses, loss =>
            loss.Kind is Compiler.Migration.ConversionLossKind.InteropPreserved
                or Compiler.Migration.ConversionLossKind.EmitterFallback);
        Assert.NotNull(conversion.Ast);
        Assert.Empty(OpaqueCodeIdentities(conversion.Ast));
        var diagnostics = new DiagnosticBag();
        var reparsed = new Parser(new Lexer(conversion.CalorSource!, diagnostics)
            .TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors);
        Assert.Empty(OpaqueCodeIdentities(reparsed));
    }

    private static ModuleNode MeasurementModule(params ExpressionNode[] expressions) =>
        new(default, "m1", "Measurement", [],
            [new FunctionNode(default, "f1", "Run", Visibility.Public, [],
                new OutputNode(default, "i32"), null,
                expressions.Select(expression =>
                    (StatementNode)new ReturnStatementNode(expression.Span, expression)).ToArray(),
                new AttributeCollection())],
            new AttributeCollection());

    [Fact]
    public void SelectedBranchMode_UsesRoslynBooleanConditions()
    {
        const string source = """
            #if A && !B
            public class SelectedAB { }
            #elif C || D
            public class SelectedCD { }
            #else
            public class SelectedFallback { }
            #endif
            """;

        AssertSelection([], "SelectedFallback");
        AssertSelection(["A"], "SelectedAB");
        AssertSelection(["A", "B"], "SelectedFallback");
        AssertSelection(["C"], "SelectedCD");
        AssertSelection(["D"], "SelectedCD");

        // Stripping deletes text: conversion spans cannot be applied to the original file.
        const string shiftedSource = """
            #if NOT_DEFINED
            public class RemovedPrefix { public int LongEnoughToShiftEveryOffset() => 12345; }
            #endif
            public class Kept
            {
                public static bool Run(bool gate) => gate && int.TryParse("1", out var value);
            }
            """;
        var options = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
            Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview,
            Microsoft.CodeAnalysis.DocumentationMode.Parse,
            Microsoft.CodeAnalysis.SourceCodeKind.Regular,
            preprocessorSymbols: Array.Empty<string>());
        var selected = Compiler.Migration.PreprocessorStripper
            .SelectActiveBranchLossy(shiftedSource, options).Source;
        var originalInvocation = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(shiftedSource, options)
            .GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>().Single();
        var selectedInvocation = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(selected, options)
            .GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>().Single();
        Assert.NotEqual(originalInvocation.Span.Start, selectedInvocation.Span.Start);
        var shiftedCoverage = new NativeConversionCoverage();
        MeasureNative("offset-shift.cs", shiftedSource, shiftedCoverage);
        var identityCoverage = new NativeConversionCoverage();
        MeasureNative("offset-selected.cs", selected, identityCoverage);
        var shifted = Assert.IsType<SourceCoverageRecord>(shiftedCoverage.LastSourceCoverage);
        var identity = Assert.IsType<SourceCoverageRecord>(identityCoverage.LastSourceCoverage);
        Assert.Equal(Hash(shiftedSource), shifted.SourceHash);
        Assert.Equal(Hash(selected), shifted.SelectedSourceHash);
        Assert.NotEqual(shifted.SourceHash, shifted.SelectedSourceHash);
        Assert.Equal(identity.SourceHash, identity.SelectedSourceHash);
        Assert.Equal(1, shifted.OpaqueBoundaries);
        Assert.Equal(selectedInvocation.DescendantNodesAndSelf()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax>().Count(),
            shifted.OpaqueSourceExpressions);
        Assert.Equal(identity.OpaqueSourceExpressionIdentityHash, shifted.OpaqueSourceExpressionIdentityHash);
        Assert.Equal(identity.OpaqueBoundaryIdentityHash, shifted.OpaqueBoundaryIdentityHash);
        Assert.Equal(identity.ExactExpressionSourceIdentityHash, shifted.ExactExpressionSourceIdentityHash);

        static void AssertSelection(
            IReadOnlyList<string> symbols,
            string expected)
        {
            var parseOptions =
                new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
                    Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview,
                    Microsoft.CodeAnalysis.DocumentationMode.Parse,
                    Microsoft.CodeAnalysis.SourceCodeKind.Regular,
                    preprocessorSymbols: symbols);
            var result =
                new Compiler.Migration.CSharpToCalorConverter(
                    new Compiler.Migration.ConversionOptions
                    {
                        Fidelity =
                            Compiler.Migration.ConversionFidelity.Lossy,
                        PreprocessorMode =
                            Compiler.Migration.PreprocessorConversionMode
                                .SelectActiveBranchLossy,
                        ParseOptions = parseOptions,
                        ModuleName = "Selection"
                    }).Convert(source, "Selection.cs");
            Assert.True(
                result.Success,
                string.Join("; ", result.Issues.Select(issue => issue.Message)));
            Assert.Contains(expected, result.CalorSource);
            foreach (var other in new[]
                     {
                         "SelectedAB",
                         "SelectedCD",
                         "SelectedFallback"
                     }.Where(name => name != expected))
                Assert.DoesNotContain(other, result.CalorSource);
        }
    }

    private static void MeasureNative(
        string file,
        string source,
        NativeConversionCoverage coverage)
    {
        coverage.FilesSeen++;
        coverage.LastSourceCoverage = null;
        var parseOptions = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
            Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview,
            Microsoft.CodeAnalysis.DocumentationMode.Parse,
            Microsoft.CodeAnalysis.SourceCodeKind.Regular,
            preprocessorSymbols: Array.Empty<string>());
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
                    ModuleName = "Leg2",
                    GracefulFallback = true,
                    AutoGenerateIds = true
                }).Convert(source, Path.GetFileName(file));
        }

        catch
        {
            coverage.ConvertExceptions++;
            return;
        }
        if (string.IsNullOrEmpty(conversion.CalorSource))
        {
            coverage.EmptyOutput++;
            return;
        }
        var diagnostics = new DiagnosticBag();
        var module = new Parser(
            new Lexer(conversion.CalorSource.Replace("\r\n", "\n"), diagnostics)
                .TokenizeAllForParser(),
            diagnostics).Parse();
        if (diagnostics.HasErrors)
        {
            coverage.OutputParseFailures++;
            return;
        }
        var bindDiagnostics = new DiagnosticBag();
        var binder = new Binder(bindDiagnostics);
        binder.Bind(module);
        Assert.NotNull(conversion.Ast);
        // Branch selection removes trivia/text and shifts offsets. Conversion AST spans
        // belong to this selected source, not the original file.
        var selectedSource = Compiler.Migration.PreprocessorStripper
            .SelectActiveBranchLossy(source, parseOptions).Source;
        var sourceTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(selectedSource, parseOptions);
        var serializedOpaque = OpaqueCodeIdentities(module).ToArray();
        coverage.LastSourceCoverage = WithBindingCoverage(MeasureSourceCoverage(
            conversion.Ast, sourceTree, selectedSource, file, binder.ExpressionsBound) with
        {
            SourceHash = Hash(source),
            SelectedSourceHash = Hash(selectedSource),
            SerializedOpaqueBoundaries = serializedOpaque.Length,
            SerializedOpaqueCodeHash = HashIdentities(serializedOpaque),
            ConversionReportedSuccess = conversion.Success,
            LossIdentityHash = HashIdentities(conversion.Losses
                .Select(loss => $"{loss.Kind}:{loss.Line}:{loss.Feature}:{loss.Description}")
                .Order(StringComparer.Ordinal))
        }, bindDiagnostics);
        AssertSourceCarriersPreserved(conversion.Ast, selectedSource, file);
        AssertOpaqueSerializationPreserved(conversion.Ast, module, file);
        coverage.ConvertedAndBound++;
        coverage.RoslynSelectedAttempted +=
            binder.ExpressionsBound;
        coverage.Incomplete += bindDiagnostics.Count(diagnostic =>
            diagnostic.Code == DiagnosticCode.AnalysisIncomplete);
    }

    private static void MeasurePreserve(
        string file,
        string source,
        Microsoft.CodeAnalysis.CSharp.CSharpParseOptions parseOptions,
        PreserveConversionCoverage coverage,
        HashSet<string> opaqueIdentities,
        HashSet<string> unconvertedIdentities)
    {
        coverage.FilesSeen++;
        Compiler.Migration.ConversionResult? conversion = null;
        var unconverted = false;
        try
        {
            conversion = new Compiler.Migration.CSharpToCalorConverter(
                new Compiler.Migration.ConversionOptions
                {
                    Fidelity = Compiler.Migration.ConversionFidelity.Lossless,
                    PreprocessorMode = Compiler.Migration.PreprocessorConversionMode
                        .PreserveAllBranches,
                    ParseOptions = parseOptions,
                    DefinedSymbols = Array.Empty<string>(),
                    ModuleName = "Leg2",
                    GracefulFallback = true,
                    AutoGenerateIds = true,
                    ValidateRoundTripCSharp = false
                }).Convert(source, Path.GetFileName(file));
        }
        catch
        {
            coverage.ConvertExceptions++;
            unconverted = true;
        }

        if (conversion?.Ast != null)
        {
            var spans = CollectOpaqueSpans(conversion.Ast, source, file, coverage);
            var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source, parseOptions);
            var root = tree.GetRoot();
            foreach (var span in spans)
            {
                var identity = $"{Path.GetFullPath(file)}:{span.Start}:{span.End}";
                Assert.True(opaqueIdentities.Add(identity),
                    $"Duplicate opaque identity: {identity}");
                coverage.OpaqueBoundaries++;
                var target = Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(
                    span.Start, span.End);
                coverage.OpaqueExpressions += root.DescendantNodes()
                    .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax>()
                    .Count(expression => target.Contains(expression.Span));
            }
        }

        if (string.IsNullOrEmpty(conversion?.CalorSource))
        {
            coverage.EmptyOutput++;
            unconverted = true;
        }
        else
        {
            var diagnostics = new DiagnosticBag();
            var reparsed = new Parser(
                new Lexer(conversion.CalorSource, diagnostics).TokenizeAllForParser(),
                diagnostics).Parse();
            if (diagnostics.HasErrors)
            {
                coverage.OutputParseFailures++;
                unconverted = true;
            }
            else if (conversion.Ast != null)
                AssertOpaqueSerializationPreserved(conversion.Ast, reparsed, file);
        }
        if (conversion is { Success: false })
            unconverted = true;
        if (unconverted)
        {
            coverage.UnconvertedFiles++;
            var identity = $"{Path.GetFullPath(file)}:0:{source.Length}";
            Assert.True(unconvertedIdentities.Add(identity),
                $"Duplicate unconverted identity: {identity}");
        }
    }

    private static IReadOnlyList<(int Start, int End)> CollectOpaqueSpans(
        Calor.Compiler.Ast.ModuleNode module,
        string source,
        string file,
        PreserveConversionCoverage coverage)
    {
        var entries = new List<(Calor.Compiler.Ast.AstNode Node, string Code)>();
        var stack = new Stack<Calor.Compiler.Ast.AstNode>();
        var seen = new HashSet<Calor.Compiler.Ast.AstNode>(
            ReferenceEqualityComparer.Instance);
        stack.Push(module);
        while (stack.TryPop(out var node))
        {
            if (!seen.Add(node))
                continue;
            switch (node)
            {
                case Calor.Compiler.Ast.CSharpInteropBlockNode interop:
                    entries.Add((node, interop.CSharpCode));
                    break;
                case Calor.Compiler.Ast.RawCSharpNode raw:
                    entries.Add((node, raw.CSharpCode));
                    break;
                case Calor.Compiler.Ast.RawCSharpExpressionNode expression:
                    entries.Add((node, expression.CSharpCode));
                    break;
            }
            foreach (var child in Calor.Compiler.Analysis.RecursiveAstWalker
                         .GetAllChildren(node))
                stack.Push(child);
        }

        var used = new List<(int Start, int End)>();
        foreach (var entry in entries
                     .OrderByDescending(entry => entry.Node.Span.Length)
                     .ThenByDescending(entry => entry.Code.Length))
        {
            var mapped = TryMapOpaqueSpan(entry.Node, entry.Code, source, used);
            if (mapped == null)
            {
                coverage.OpaqueUnmapped++;
                continue;
            }
            if (entry.Node is RawCSharpExpressionNode)
                AssertRawExpressionSourcePreserved(
                    entry.Code, source[mapped.Value.Start..mapped.Value.End], file);
            if (used.Any(existing => mapped.Value.Start >= existing.Start
                && mapped.Value.End <= existing.End))
                continue;
            Assert.False(used.Any(existing => mapped.Value.Start < existing.End
                && mapped.Value.End > existing.Start),
                $"Partially overlapping opaque spans in {file}: "
                + $"{mapped.Value.Start}..{mapped.Value.End}");
            used.Add(mapped.Value);
        }
        return used.OrderBy(span => span.Start).ToArray();
    }

    private static (int Start, int End)? TryMapOpaqueSpan(
        Calor.Compiler.Ast.AstNode node,
        string code,
        string source,
        IReadOnlyList<(int Start, int End)> used)
    {
        if (node.Span.Length > 0
            && node.Span.Start >= 0
            && node.Span.End <= source.Length)
        {
            var span = (node.Span.Start, node.Span.End);
            return span;
        }
        foreach (var candidate in new[] { code, code.Trim() }
                     .Where(candidate => candidate.Length > 0)
                     .Distinct(StringComparer.Ordinal))
        {
            var search = 0;
            while (search <= source.Length - candidate.Length)
            {
                var start = source.IndexOf(candidate, search, StringComparison.Ordinal);
                if (start < 0)
                    break;
                var span = (Start: start, End: start + candidate.Length);
                if (!used.Any(existing =>
                        span.Start < existing.End
                        && span.End > existing.Start)
                    || used.Any(existing =>
                        span.Start >= existing.Start
                        && span.End <= existing.End))
                    return span;
                search = start + 1;
            }
        }
        return null;
    }

    private const int LegacySourceOrderAttempted = 18005;

    private static void AssertRawExpressionSourcePreserved(string code, string source, string file)
    {
        static IEnumerable<string> Tokens(string text)
        {
            var expression = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression(text);
            Assert.False(expression.ContainsDiagnostics, "Opaque expression must parse in full.");
            while (expression is Microsoft.CodeAnalysis.CSharp.Syntax.ParenthesizedExpressionSyntax parenthesized)
                expression = parenthesized.Expression;
            return expression.DescendantTokens().Select(token => $"{token.RawKind}:{token.Text}");
        }

        Assert.True(Tokens(code).SequenceEqual(Tokens(source)),
            $"Opaque expression is not the source expression at its recorded span in {file}.");
    }

    // Exact source-span matches are provenance, not a semantic-fidelity certificate:
    // some callees live in strings and generated references can reuse source spans.
    // Pinning identities (not only cardinalities) detects loss/swaps even at equal totals.
    private static SourceCoverageRecord MeasureSourceCoverage(
        ModuleNode module,
        Microsoft.CodeAnalysis.SyntaxTree sourceTree,
        string source,
        string file,
        int binderAttempts)
    {
        var opaqueCoverage = new PreserveConversionCoverage();
        var opaque = CollectOpaqueSpans(module, source, file, opaqueCoverage);
        Assert.Equal(0, opaqueCoverage.OpaqueUnmapped);
        var mapped = Walk(module).OfType<ExpressionNode>()
            .Where(node => node is not RawCSharpExpressionNode)
            .Select(node => (node.Span.Start, node.Span.End)).ToHashSet();
        var expressions = sourceTree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax>()
            .ToArray();
        var representedIdentities = new List<string>();
        var opaqueIdentities = new List<string>();
        var unmappedIdentities = new List<string>();
        var mixedIdentities = new List<string>();
        foreach (var expression in expressions)
        {
            var identity = $"{expression.Span.Start}:{expression.Span.End}:{expression.RawKind}";
            if (opaque.Any(span =>
                    span.Start <= expression.Span.Start && span.End >= expression.Span.End))
                opaqueIdentities.Add(identity);
            else if (mapped.Contains((expression.Span.Start, expression.Span.End)))
            {
                representedIdentities.Add(identity);
                if (opaque.Any(span =>
                        expression.Span.Start <= span.Start && expression.Span.End >= span.End))
                    mixedIdentities.Add(identity);
            }
            else
                unmappedIdentities.Add(identity);
        }
        return new SourceCoverageRecord(
            Hash(source), binderAttempts, expressions.Length,
            representedIdentities.Count, HashIdentities(representedIdentities),
            opaqueIdentities.Count, HashIdentities(opaqueIdentities),
            opaque.Count, HashIdentities(opaque.Select(span => $"{span.Start}:{span.End}")))
        {
            UnmappedSourceExpressions = unmappedIdentities.Count,
            UnmappedSourceIdentityHash = HashIdentities(unmappedIdentities),
            ExactExpressionsWithOpaqueDescendants = mixedIdentities.Count,
            MixedExpressionSourceIdentityHash = HashIdentities(mixedIdentities)
        };
    }

    private static SourceCoverageRecord WithBindingCoverage(
        SourceCoverageRecord coverage, DiagnosticBag diagnostics)
    {
        static string ErrorIdentities(DiagnosticBag bag) => HashIdentities(bag.Errors
            .Select(error => $"{error.Code}:{error.Span.Start}:{error.Span.End}:{error.Message}")
            .Order(StringComparer.Ordinal));
        var propagated = new DiagnosticBag();
        BindingDiagnosticPolicy.PropagateCompilationErrors(diagnostics, propagated);
        return coverage with
        {
            BindingErrorCount = diagnostics.Errors.Count(),
            BindingErrorIdentityHash = ErrorIdentities(diagnostics),
            PropagatedBindingErrorCount = propagated.Errors.Count(),
            PropagatedBindingErrorIdentityHash = ErrorIdentities(propagated)
        };
    }

    private static void AssertSourceCarriersPreserved(ModuleNode module, string source, string file)
    {
        var relative = Path.GetRelativePath(Path.Combine(RepoRoot(), "bench", "corpus"), file)
            .Replace('\\', '/');
        var nodes = Walk(module).OfType<ExpressionNode>().ToArray();
        foreach (var evidence in SourceCarrierCases.Value.Where(c => c.File == relative))
            AssertSourceCarrierPreserved(nodes, source, evidence);
    }

    private static void AssertSourceCarrierPreserved(
        IReadOnlyList<ExpressionNode> nodes, string source, SourceCarrierCase evidence)
    {
        Assert.Equal(evidence.SourceExpression, source[evidence.SourceStart..evidence.SourceEnd]);
        var candidates = nodes.Where(node => node.GetType().Name == evidence.CarrierKind
            && node.Span.Start == evidence.CarrierStart && node.Span.End == evidence.CarrierEnd);
        Assert.True(candidates.Any(node => OpaqueTokens(
                node.Accept(new Compiler.CodeGen.CSharpEmitter()))
            .SequenceEqual(OpaqueTokens(evidence.CarrierCode))),
            $"Source carrier disappeared or changed for {evidence.File}:"
            + $"{evidence.SourceStart}..{evidence.SourceEnd} ({evidence.SourceKind}). "
            + "Resolve the source operation explicitly; regenerating aggregate coverage is not sufficient.");
    }

    private static void AssertOpaqueSerializationPreserved(
        ModuleNode converted, ModuleNode reparsed, string file)
    {
        // The emitter can introduce additional raw expressions. They are separately
        // pinned in SourceCoverageRecord, never silently counted as native source.
        var remaining = OpaqueCodeIdentities(reparsed).ToList();
        foreach (var identity in OpaqueCodeIdentities(converted))
            Assert.True(remaining.Remove(identity),
                $"Opaque C# changed or disappeared during Calor serialization in {file}.");
    }

    private static IEnumerable<string> OpaqueCodeIdentities(ModuleNode module) =>
        Walk(module).Select(node => node switch
                {
                    RawCSharpExpressionNode expression => expression.CSharpCode,
                    RawCSharpNode statement => statement.CSharpCode,
                    CSharpInteropBlockNode block => block.CSharpCode,
                    _ => null
                })
                .Where(code => code != null)
                .Select(code => HashIdentities(OpaqueTokens(code!)))
                .Order(StringComparer.Ordinal);

    private static IEnumerable<string> OpaqueTokens(string code)
    {
        static IEnumerable<string> Directives(Microsoft.CodeAnalysis.SyntaxTriviaList triviaList) =>
            triviaList.Where(trivia => trivia.IsDirective
                    || trivia.RawKind == (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.DisabledTextTrivia)
                .Select(trivia =>
                    $"trivia:{trivia.RawKind}:{trivia.ToFullString().Replace("\r\n", "\n").Trim()}");

        foreach (var token in Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseTokens(code))
        {
            foreach (var trivia in Directives(token.LeadingTrivia))
                yield return trivia;
            yield return $"{token.RawKind}:{token.Text}";
            foreach (var trivia in Directives(token.TrailingTrivia))
                yield return trivia;
        }
    }

    private static IEnumerable<AstNode> Walk(AstNode root)
    {
        var stack = new Stack<AstNode>();
        var seen = new HashSet<AstNode>(ReferenceEqualityComparer.Instance);
        stack.Push(root);
        while (stack.TryPop(out var node))
        {
            if (!seen.Add(node))
                continue;
            yield return node;
            foreach (var child in Compiler.Analysis.RecursiveAstWalker.GetAllChildren(node))
                stack.Push(child);
        }
    }

    private static string Hash(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string HashIdentities(IEnumerable<string> identities) =>
        Hash(string.Join("\n", identities));

    private sealed record SourceCoverageRecord(
        string SourceHash,
        int BinderAttempts,
        int SourceExpressions,
        int ExactExpressionSourceSpans,
        string ExactExpressionSourceIdentityHash,
        int OpaqueSourceExpressions,
        string OpaqueSourceExpressionIdentityHash,
        int OpaqueBoundaries,
        string OpaqueBoundaryIdentityHash,
        int SerializedOpaqueBoundaries = 0,
        string SerializedOpaqueCodeHash = "",
        string LossIdentityHash = "",
        bool ConversionReportedSuccess = false,
        string SelectedSourceHash = "",
        int BindingErrorCount = 0,
        string BindingErrorIdentityHash = "",
        int PropagatedBindingErrorCount = 0,
        string PropagatedBindingErrorIdentityHash = "",
        int UnmappedSourceExpressions = 0,
        string UnmappedSourceIdentityHash = "",
        int ExactExpressionsWithOpaqueDescendants = 0,
        string MixedExpressionSourceIdentityHash = "");

    private sealed record SourceCarrierEvidence(IReadOnlyList<SourceCarrierCase> Cases);

    private sealed record SourceCarrierCase(
        string File,
        int SourceStart,
        int SourceEnd,
        string SourceKind,
        string SourceExpression,
        string CarrierKind,
        int CarrierStart,
        int CarrierEnd,
        string CarrierCode);

    private sealed class NativeConversionCoverage
    {
        public int Incomplete;
        public int RoslynSelectedAttempted;
        public int FilesSeen;
        public int ConvertedAndBound;
        public int ConvertExceptions;
        public int EmptyOutput;
        public int OutputParseFailures;
        public SourceCoverageRecord? LastSourceCoverage;
        public NativeConversionLeg ToRecord() => new(
            Incomplete,
            LegacySourceOrderAttempted,
            RoslynSelectedAttempted,
            FilesSeen,
            ConvertedAndBound,
            ConvertExceptions,
            EmptyOutput,
            OutputParseFailures);
    }

    private sealed class PreserveConversionCoverage
    {
        public int FilesSeen;
        public int OpaqueBoundaries;
        public int OpaqueExpressions;
        public int OpaqueUnmapped;
        public int OpaqueIdentityCount;
        public int UnconvertedFiles;
        public int UnconvertedIdentityCount;
        public int ConvertExceptions;
        public int EmptyOutput;
        public int OutputParseFailures;
        public PreserveCoverageLeg ToRecord() => new(
            FilesSeen,
            OpaqueBoundaries,
            OpaqueExpressions,
            OpaqueUnmapped,
            OpaqueIdentityCount,
            UnconvertedFiles,
            UnconvertedIdentityCount,
            ConvertExceptions,
            EmptyOutput,
            OutputParseFailures);
    }

    private sealed record NativeConversionLeg(
        int Incomplete,
        int LegacySourceOrderAttempted,
        int RoslynSelectedAttempted,
        int FilesSeen,
        int ConvertedAndBound,
        int ConvertExceptions,
        int EmptyOutput,
        int OutputParseFailures);

    private sealed record PreserveCoverageLeg(
        int FilesSeen,
        int OpaqueBoundaries,
        int OpaqueExpressions,
        int OpaqueUnmapped,
        int OpaqueIdentityCount,
        int UnconvertedFiles,
        int UnconvertedIdentityCount,
        int ConvertExceptions,
        int EmptyOutput,
        int OutputParseFailures);

    private sealed record Baseline(
        int IncompleteCount,
        int ParsedFiles,
        int ParseFailures,
        int ExpressionsBound,
        string Scope,
        NativeConversionLeg? Conversion = null,
        PreserveCoverageLeg? PreserveCoverage = null);
}
